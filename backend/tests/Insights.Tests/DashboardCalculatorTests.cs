using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Insights.Api.Dashboard;
using Insights.Api.Data;
using Insights.Api.Projection;

namespace Insights.Tests;

public class DashboardCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
    private static readonly Dictionary<string, FxRate> Rates = FxRate.Seed.ToDictionary(r => r.CurrencyCode);

    private static ClaimSnapshot Claim(string market, string currency, decimal reserve, string status = "UnderReview",
        string? handler = "handler-1", int ageDays = 1, string type = "Motor") => new()
    {
        ClaimId = Guid.NewGuid(), Reference = "CLM", MarketCode = market, CurrencyCode = currency, ClaimType = type,
        Status = status, HandlerId = handler, HandlerName = handler, ReserveAmount = reserve,
        SubmittedAt = Now.AddDays(-ageDays),
    };

    [Fact]
    public void Exposure_is_reported_in_local_currency_with_usd_headline()
    {
        var claims = new[]
        {
            Claim("SG", "SGD", 1_000.00m),
            Claim("SG", "SGD", 500.00m, type: "Property"),
            Claim("KR", "KRW", 3_500_000m),
            Claim("SG", "SGD", 9_999m, status: "Settled"),   // closed: excluded
            Claim("KR", "KRW", 1_000_000m, status: "Rejected"),
        };

        var report = DashboardCalculator.Exposure(claims, Rates);

        var sg = report.Markets.Single(m => m.MarketCode == "SG");
        Assert.Equal(1_500.00m, sg.Amount);          // local currency, not converted
        Assert.Equal(1_155.00m, sg.AmountUsd);       // 1500 × 0.77
        var kr = report.Markets.Single(m => m.MarketCode == "KR");
        Assert.Equal(3_500_000m, kr.Amount);
        Assert.Equal(2_520.00m, kr.AmountUsd);       // 3.5M × 0.00072

        Assert.Equal(3, report.OpenClaims);
        Assert.Equal(3_675.00m, report.TotalUsd);
        Assert.Contains("Indicative", report.RatesNote);
    }

    [Fact]
    public void Approved_but_unpaid_claims_still_count_as_exposure()
    {
        var report = DashboardCalculator.Exposure([Claim("HK", "HKD", 1_000m, status: "Approved")], Rates);
        Assert.Equal(1_000m, report.Markets.Single().Amount);
    }

    [Fact]
    public void Workload_counts_open_claims_per_handler_and_the_unassigned_queue()
    {
        var claims = new[]
        {
            Claim("SG", "SGD", 100m, handler: "handler-1", ageDays: 3),
            Claim("SG", "SGD", 100m, status: "AwaitingInfo", handler: "handler-1", ageDays: 12),
            Claim("TH", "THB", 100m, handler: "handler-2"),
            Claim("TH", "THB", 100m, status: "Submitted", handler: null, ageDays: 6),
            Claim("TH", "THB", 100m, status: "Settled", handler: "handler-2"),
        };

        var w = DashboardCalculator.Workload(claims, Rates, Now);

        Assert.Equal(1, w.UnassignedCount);
        Assert.Equal(6, w.OldestUnassignedDays);
        var h1 = w.Handlers.Single(h => h.HandlerId == "handler-1");
        Assert.Equal((2, 1, 12), (h1.OpenClaims, h1.AwaitingInfo, h1.OldestOpenDays));
        Assert.Equal(1, w.Handlers.Single(h => h.HandlerId == "handler-2").OpenClaims);
    }

    [Fact]
    public void Ageing_buckets_have_no_gaps_at_the_boundaries()
    {
        var claims = new[] { 0, 7, 8, 30, 31, 90 }.Select(d => Claim("SG", "SGD", 1m, ageDays: d)).ToArray();
        var buckets = DashboardCalculator.Ageing(claims, Rates, Now);
        Assert.Equal([2, 2, 2], buckets.Select(b => b.Count));
    }

    // ------------------------------------------------------------------ properties

    private static readonly (string Market, string Currency)[] Markets =
        [("KR", "KRW"), ("HK", "HKD"), ("TW", "TWD"), ("TH", "THB"), ("MY", "MYR"), ("SG", "SGD")];
    private static readonly string[] Statuses = ["Submitted", "UnderReview", "AwaitingInfo", "Approved", "Rejected", "Settled"];

    private static Arbitrary<ClaimSnapshot[]> Portfolios() =>
        Gen.ArrayOf(
            from m in Gen.Elements(Markets)
            from status in Gen.Elements(Statuses)
            from units in Gen.Choose(0, 50_000_000)
            from age in Gen.Choose(0, 400)
            select Claim(m.Market, m.Currency, m.Currency == "KRW" ? units : units / 100m, status, ageDays: age))
        .ToArbitrary();

    [Property(MaxTest = 300)]
    public Property Headline_usd_equals_the_sum_of_market_rows_and_only_counts_open_claims() =>
        Prop.ForAll(Portfolios(), claims =>
        {
            var r = DashboardCalculator.Exposure(claims, Rates);
            var open = claims.Where(c => c.IsOpen).ToList();

            return r.TotalUsd == r.Markets.Sum(m => m.AmountUsd)
                   && r.OpenClaims == open.Count
                   && r.Markets.Sum(m => m.OpenClaims) == open.Count
                   && r.Markets.All(m => m.Amount == open.Where(c => c.MarketCode == m.MarketCode).Sum(c => c.ReserveAmount))
                   && r.TotalUsd >= 0;
        });

    [Property(MaxTest = 300)]
    public Property Every_open_claim_lands_in_exactly_one_ageing_bucket() =>
        Prop.ForAll(Portfolios(), claims =>
            DashboardCalculator.Ageing(claims, Rates, Now).Sum(b => b.Count) == claims.Count(c => c.IsOpen));
}
