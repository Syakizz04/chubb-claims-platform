using Insights.Api.Data;
using Insights.Api.Projection;

namespace Insights.Api.Dashboard;

// ------------------------------------------------------------------ response models

/// <summary>M-1 / M-2: exposure in each market's own currency, plus USD as the headline total.</summary>
public record ExposureReport(
    decimal TotalUsd,
    int OpenClaims,
    DateOnly RatesAsOf,
    string RatesNote,
    IReadOnlyList<MarketExposure> Markets,
    IReadOnlyList<TypeExposure> ByType,
    IReadOnlyList<string> CurrenciesWithoutRate); // markets reported in local currency only, left out of USD totals

public record MarketExposure(
    string MarketCode,
    string CurrencyCode,
    decimal Amount,      // local currency: the real liability
    decimal? AmountUsd,  // indicative; null when Insights has no FX rate for the currency
    int OpenClaims,
    IReadOnlyList<TypeExposure> ByType);

public record TypeExposure(string ClaimType, decimal? AmountUsd, int OpenClaims, decimal? Amount = null, string? CurrencyCode = null);

/// <summary>M-3: who is carrying what.</summary>
public record WorkloadReport(
    int UnassignedCount,
    int? OldestUnassignedDays,
    IReadOnlyList<HandlerWorkload> Handlers);

public record HandlerWorkload(
    string HandlerId,
    string HandlerName,
    int OpenClaims,
    int AwaitingInfo,
    int? OldestOpenDays,
    decimal ReserveUsd);

/// <summary>M-4: open claims by age since submission.</summary>
public record AgeingBucket(string Label, int MinDays, int? MaxDays, int Count, decimal ReserveUsd);

/// <summary>M-5: decisions in the last 30 days.</summary>
public record HandlerPerformance(
    string HandlerId,
    string HandlerName,
    int Decided30d,
    int Approved30d,
    int Rejected30d,
    double? AvgDaysToDecision);

public record DashboardReport(
    DateTimeOffset GeneratedAt,
    DateTimeOffset? LastProcessedAt, // M-6: when Insights last applied an event, so read-model staleness is visible
    ExposureReport Exposure,
    WorkloadReport Workload,
    IReadOnlyList<AgeingBucket> Ageing,
    IReadOnlyList<HandlerPerformance> Performance);

/// <summary>
/// Pure aggregation over snapshots: no I/O, so it is unit and property tested directly.
/// At this scale, aggregating snapshots on each read is simple and always correct. At larger scale
/// we'd keep pre-aggregated counters updated by the projection instead.
/// </summary>
public static class DashboardCalculator
{
    /// <summary>
    /// Indicative USD, or null when there's no rate for the currency. A market added on the Claims
    /// side before its rate reaches Insights must not take the whole dashboard down. The local-currency
    /// liability is still reported; it's just left out of the USD totals, and the report says so.
    /// </summary>
    public static decimal? ToUsd(decimal amount, string currency, IReadOnlyDictionary<string, FxRate> rates) =>
        rates.TryGetValue(currency, out var r)
            ? decimal.Round(amount * r.UsdPerUnit, 2, MidpointRounding.ToEven)
            : null;

    public static ExposureReport Exposure(IReadOnlyCollection<ClaimSnapshot> claims, IReadOnlyDictionary<string, FxRate> rates)
    {
        var open = claims.Where(c => c.IsOpen).ToList();

        var markets = open
            .GroupBy(c => (c.MarketCode, c.CurrencyCode))
            .Select(g =>
            {
                var local = g.Sum(c => c.ReserveAmount);
                var byType = g.GroupBy(c => c.ClaimType)
                    .Select(t => new TypeExposure(t.Key, ToUsd(t.Sum(c => c.ReserveAmount), g.Key.CurrencyCode, rates),
                        t.Count(), t.Sum(c => c.ReserveAmount), g.Key.CurrencyCode))
                    .OrderBy(t => t.ClaimType).ToList();
                return new MarketExposure(g.Key.MarketCode, g.Key.CurrencyCode, local,
                    ToUsd(local, g.Key.CurrencyCode, rates), g.Count(), byType);
            })
            .OrderByDescending(m => m.AmountUsd ?? -1) // unrated markets last
            .ToList();

        var byTypeTotal = markets.SelectMany(m => m.ByType)
            .GroupBy(t => t.ClaimType)
            .Select(g => new TypeExposure(g.Key, g.Sum(t => t.AmountUsd ?? 0), g.Sum(t => t.OpenClaims)))
            .OrderBy(t => t.ClaimType).ToList();

        var asOf = rates.Values.Select(r => r.AsOf).DefaultIfEmpty().Min();

        return new ExposureReport(
            // Sum of the per-market USD figures, so the headline always equals the sum of the table rows.
            markets.Sum(m => m.AmountUsd ?? 0),
            open.Count,
            asOf,
            $"Indicative only. Converted at static rates as of {asOf:yyyy-MM-dd}. The liability is owed in local currency.",
            markets,
            byTypeTotal,
            markets.Where(m => m.AmountUsd is null).Select(m => m.CurrencyCode).Distinct().Order().ToList());
    }

    public static WorkloadReport Workload(IReadOnlyCollection<ClaimSnapshot> claims, IReadOnlyDictionary<string, FxRate> rates, DateTimeOffset now)
    {
        var open = claims.Where(c => c.IsOpen).ToList();
        var unassigned = open.Where(c => c.HandlerId is null).ToList();

        var handlers = open.Where(c => c.HandlerId is not null)
            .GroupBy(c => c.HandlerId!)
            .Select(g => new HandlerWorkload(
                g.Key,
                g.Select(c => c.HandlerName).FirstOrDefault(n => n is not null) ?? g.Key,
                g.Count(),
                g.Count(c => c.Status == "AwaitingInfo"),
                g.Max(c => AgeDays(c.SubmittedAt, now)),
                g.Sum(c => ToUsd(c.ReserveAmount, c.CurrencyCode, rates) ?? 0)))
            .OrderByDescending(h => h.OpenClaims).ThenBy(h => h.HandlerName)
            .ToList();

        return new WorkloadReport(unassigned.Count,
            unassigned.Count == 0 ? null : unassigned.Max(c => AgeDays(c.SubmittedAt, now)), handlers);
    }

    public static IReadOnlyList<AgeingBucket> Ageing(IReadOnlyCollection<ClaimSnapshot> claims, IReadOnlyDictionary<string, FxRate> rates, DateTimeOffset now)
    {
        (string Label, int Min, int? Max)[] buckets = [("0–7 days", 0, 7), ("8–30 days", 8, 30), ("31+ days", 31, null)];
        var open = claims.Where(c => c.IsOpen).Select(c => (Claim: c, Age: AgeDays(c.SubmittedAt, now))).ToList();

        return buckets.Select(b =>
        {
            var inBucket = open.Where(x => x.Age >= b.Min && (b.Max is null || x.Age <= b.Max)).ToList();
            return new AgeingBucket(b.Label, b.Min, b.Max, inBucket.Count,
                inBucket.Sum(x => ToUsd(x.Claim.ReserveAmount, x.Claim.CurrencyCode, rates) ?? 0));
        }).ToList();
    }

    public static IReadOnlyList<HandlerPerformance> Performance(IReadOnlyCollection<ClaimSnapshot> claims, DateTimeOffset now)
    {
        var since = now.AddDays(-30);
        return claims
            .Where(c => c.HandlerId is not null && c.DecidedAt is not null && c.DecidedAt >= since)
            .GroupBy(c => c.HandlerId!)
            .Select(g => new HandlerPerformance(
                g.Key,
                g.Select(c => c.HandlerName).FirstOrDefault(n => n is not null) ?? g.Key,
                g.Count(),
                g.Count(c => c.ApprovedAmount is not null),
                g.Count(c => c.Status == "Rejected"),
                Math.Round(g.Average(c => (c.DecidedAt!.Value - c.SubmittedAt).TotalDays), 1)))
            .OrderByDescending(p => p.Decided30d)
            .ToList();
    }

    public static int AgeDays(DateTimeOffset submittedAt, DateTimeOffset now) => Math.Max(0, (int)(now - submittedAt).TotalDays);
}
