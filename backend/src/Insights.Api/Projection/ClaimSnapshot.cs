using Claims.Contracts;

namespace Insights.Api.Projection;

/// <summary>
/// Insights' own read model of a claim: only the fields dashboards need. Built entirely from
/// Kafka events; Insights never reads the Claims database (database-per-service).
/// </summary>
public class ClaimSnapshot
{
    public Guid ClaimId { get; set; }
    public string Reference { get; set; } = "";
    public string MarketCode { get; set; } = "";
    public string CurrencyCode { get; set; } = "";
    public string ClaimType { get; set; } = "";
    public string Status { get; set; } = "";
    public string ClaimantId { get; set; } = "";
    public string? HandlerId { get; set; }
    public string? HandlerName { get; set; }
    public decimal ClaimedAmount { get; set; }
    public decimal ReserveAmount { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public decimal? SettledAmount { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>The highest event sequence applied. Older or duplicate events are ignored.</summary>
    public int LastSequence { get; set; }
    public DateTimeOffset LastEventAt { get; set; }

    public static readonly string[] TerminalStatuses = ["Settled", "Rejected"];
    public bool IsOpen => !TerminalStatuses.Contains(Status);
}

/// <summary>
/// Pure projection logic, with no I/O, so it can be property tested.
/// Because each event carries the full claim snapshot, applying an event is just "take the newer
/// snapshot". That makes the projection idempotent (re-applying is a no-op) and tolerant of
/// out-of-order delivery (an older event never overwrites a newer one).
/// </summary>
public static class ClaimProjection
{
    /// <returns>True if the snapshot changed.</returns>
    public static bool Apply(ClaimSnapshot snapshot, ClaimEventMessage e)
    {
        if (e.Sequence <= snapshot.LastSequence) return false; // duplicate or stale

        snapshot.ClaimId = e.ClaimId;
        snapshot.Reference = e.Reference;
        snapshot.MarketCode = e.MarketCode;
        snapshot.CurrencyCode = e.CurrencyCode;
        snapshot.ClaimType = e.ClaimType;
        snapshot.Status = e.Status;
        snapshot.ClaimantId = e.ClaimantId;
        snapshot.HandlerId = e.HandlerId;
        snapshot.HandlerName = e.HandlerName;
        snapshot.ClaimedAmount = e.ClaimedAmount;
        snapshot.ReserveAmount = e.ReserveAmount;
        snapshot.ApprovedAmount = e.ApprovedAmount;
        snapshot.SettledAmount = e.SettledAmount;
        snapshot.SubmittedAt = e.SubmittedAt;
        snapshot.DecidedAt = e.DecidedAt;
        snapshot.ClosedAt = e.ClosedAt;
        snapshot.LastSequence = e.Sequence;
        snapshot.LastEventAt = e.OccurredAt;
        return true;
    }

    /// <summary>E-5: which events should notify the claimant, and with what message.</summary>
    public static string? NotificationFor(ClaimEventMessage e)
    {
        string Money(decimal? amount) => $"{e.CurrencyCode} {(amount ?? 0).ToString($"N{e.CurrencyMinorUnits}")}";
        return e.EventType switch
        {
            "Submitted" => $"We've received your claim {e.Reference}.",
            "InformationRequested" => $"We need more information on claim {e.Reference}: {e.PublicNote}",
            "Approved" => $"Good news: claim {e.Reference} has been approved for {Money(e.ApprovedAmount)}.",
            "Rejected" => $"Claim {e.Reference} was not approved. Reason: {e.PublicNote}",
            "Settled" => $"Claim {e.Reference} has been settled: {Money(e.SettledAmount)} is on its way.",
            _ => null,
        };
    }
}
