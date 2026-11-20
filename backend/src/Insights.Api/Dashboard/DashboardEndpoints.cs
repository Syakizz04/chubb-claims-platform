using Insights.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Insights.Api.Dashboard;

/// <summary>
/// Read-only dashboard API, served from Insights' own read model. Figures are eventually consistent
/// with the Claims service; LastProcessedAt makes the lag visible (M-6).
///
/// Dev identity: Insights has no user store of its own. In production both services would validate
/// the same JWT and enforce the Manager role here.
/// </summary>
public static class DashboardEndpoints
{
    public static void MapDashboardApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/insights").WithTags("Insights");

        api.MapGet("/dashboard", async (InsightsDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow();
            var claims = await db.Claims.AsNoTracking().ToListAsync(ct);
            var rates = await db.FxRates.AsNoTracking().ToDictionaryAsync(r => r.CurrencyCode, ct);
            var lastProcessedAt = await db.ProcessedEvents.MaxAsync(p => (DateTimeOffset?)p.ProcessedAt, ct);

            return new DashboardReport(now, lastProcessedAt,
                DashboardCalculator.Exposure(claims, rates),
                DashboardCalculator.Workload(claims, rates, now),
                DashboardCalculator.Ageing(claims, rates, now),
                DashboardCalculator.Performance(claims, now));
        }).WithSummary("M-1..M-6: exposure (local + USD), workload, ageing and performance in one call");

        api.MapGet("/exposure", async (InsightsDbContext db, CancellationToken ct) =>
                DashboardCalculator.Exposure(await db.Claims.AsNoTracking().ToListAsync(ct),
                    await db.FxRates.AsNoTracking().ToDictionaryAsync(r => r.CurrencyCode, ct)))
            .WithSummary("M-1 / M-2: outstanding exposure");

        api.MapGet("/fx-rates", async (InsightsDbContext db, CancellationToken ct) =>
                await db.FxRates.AsNoTracking().OrderBy(r => r.CurrencyCode).ToListAsync(ct))
            .WithSummary("Static seeded FX rates used for the indicative USD figures");

        api.MapGet("/notifications", async (HttpRequest req, InsightsDbContext db, CancellationToken ct) =>
            {
                var claimantId = req.Headers["X-User-Id"].ToString();
                if (string.IsNullOrWhiteSpace(claimantId)) return Results.Unauthorized();
                return Results.Ok(await db.Notifications.AsNoTracking()
                    .Where(n => n.ClaimantId == claimantId)
                    .OrderByDescending(n => n.CreatedAt).Take(50)
                    .Select(n => new { n.Id, n.ClaimId, n.Reference, n.Message, n.CreatedAt })
                    .ToListAsync(ct));
            })
            .WithSummary("E-5: the claimant's notifications (newest first)");
    }
}
