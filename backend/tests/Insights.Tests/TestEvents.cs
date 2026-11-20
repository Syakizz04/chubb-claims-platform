using Claims.Contracts;

namespace Insights.Tests;

/// <summary>Builds realistic event streams for one claim, as the Claims service would publish them.</summary>
public static class TestEvents
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    public static ClaimEventMessage Event(Guid claimId, int seq, string type, string status, decimal reserve,
        string market = "SG", string currency = "SGD", string? handler = "handler-1",
        decimal? approved = null, decimal? settled = null, int submittedDaysAgo = 0, DateTimeOffset? now = null) => new()
    {
        EventId = Guid.NewGuid(),
        EventType = type,
        OccurredAt = T0.AddHours(seq),
        ActorId = handler ?? "claimant",
        ClaimId = claimId,
        Sequence = seq,
        Reference = $"CLM-{market}-{seq:D6}",
        MarketCode = market,
        CurrencyCode = currency,
        CurrencyMinorUnits = currency == "KRW" ? 0 : 2,
        ClaimType = "Motor",
        Status = status,
        ClaimantId = "claimant-sg",
        HandlerId = seq == 1 ? null : handler,
        HandlerName = seq == 1 ? null : handler,
        ClaimedAmount = 1000m,
        ReserveAmount = reserve,
        ApprovedAmount = approved,
        SettledAmount = settled,
        SubmittedAt = (now ?? T0).AddDays(-submittedDaysAgo),
        DecidedAt = approved is not null || status == "Rejected" ? T0.AddHours(seq) : null,
        ClosedAt = status is "Settled" or "Rejected" ? T0.AddHours(seq) : null,
    };

    /// <summary>submit → pick up → reserve change → approve → settle.</summary>
    public static List<ClaimEventMessage> FullLifecycle(Guid? id = null)
    {
        var c = id ?? Guid.NewGuid();
        return
        [
            Event(c, 1, "Submitted", "Submitted", 1000m),
            Event(c, 2, "PickedUp", "UnderReview", 1000m),
            Event(c, 3, "ReserveAdjusted", "UnderReview", 800m),
            Event(c, 4, "Approved", "Approved", 750m, approved: 750m),
            Event(c, 5, "Settled", "Settled", 0m, approved: 750m, settled: 750m),
        ];
    }
}
