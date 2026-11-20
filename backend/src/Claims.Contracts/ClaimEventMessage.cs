namespace Claims.Contracts;

/// <summary>
/// The integration event published by the Claims service on the <see cref="Topics.ClaimEvents"/> topic.
///
/// Design: "event-carried state transfer". Each message carries the claim's full state after the
/// event (what happened, plus a snapshot), so consumers never need to call back into the Claims API.
/// That's what lets Insights run when Claims is down, and the other way round (E-4).
///
/// Consumers must be idempotent: use <see cref="EventId"/> to drop duplicates and
/// <see cref="Sequence"/> to ignore stale snapshots (E-3).
///
/// Versioning: additive changes only. Breaking changes need a new topic or a schema version field.
/// </summary>
public sealed record ClaimEventMessage
{
    public required Guid EventId { get; init; }
    public required string EventType { get; init; }       // Submitted, PickedUp, Approved, …
    public required DateTimeOffset OccurredAt { get; init; }
    public required string ActorId { get; init; }

    public required Guid ClaimId { get; init; }
    public required int Sequence { get; init; }           // per-claim, 1-based, gap-free
    public required string Reference { get; init; }
    public required string MarketCode { get; init; }
    public required string CurrencyCode { get; init; }
    /// <summary>ISO 4217 minor units, so consumers can format amounts without hard-coding currencies.</summary>
    public int CurrencyMinorUnits { get; init; } = 2;
    public required string ClaimType { get; init; }
    public required string Status { get; init; }
    public string? PreviousStatus { get; init; }

    public required string ClaimantId { get; init; }
    public string? HandlerId { get; init; }
    public string? HandlerName { get; init; }

    public required decimal ClaimedAmount { get; init; }
    public required decimal ReserveAmount { get; init; }
    public decimal? ApprovedAmount { get; init; }
    public decimal? SettledAmount { get; init; }

    public required DateTimeOffset SubmittedAt { get; init; }
    public DateTimeOffset? DecidedAt { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }

    /// <summary>Only set for claimant-visible events (e.g. the info request question, the rejection reason).</summary>
    public string? PublicNote { get; init; }
}

public static class Topics
{
    public const string ClaimEvents = "claims.events";
}
