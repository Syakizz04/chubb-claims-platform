namespace Claims.Api.Domain;

/// <summary>
/// Immutable audit record of something that happened to a claim (DR-6). It is the source of
/// both timelines and of the integration events published to Kafka (E-1).
/// </summary>
public class ClaimEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ClaimId { get; init; }

    /// <summary>1, 2, 3… per claim. Lets consumers ignore stale or duplicate events.</summary>
    public int Sequence { get; init; }

    public ClaimEventType Type { get; init; }
    public ClaimStatus? FromStatus { get; init; }
    public ClaimStatus? ToStatus { get; init; }
    public required string ActorId { get; init; }
    public string? Note { get; init; }
    public decimal? Amount { get; init; }
    public decimal? PreviousAmount { get; init; }

    /// <summary>Internal events (reserve changes, notes) are never shown to claimants (C-4).</summary>
    public bool IsInternal { get; init; }

    public DateTimeOffset OccurredAt { get; init; }
}

/// <summary>A handler's question to the claimant and the claimant's answer (H-5, C-5).</summary>
public class InformationRequest
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ClaimId { get; init; }
    public required string Question { get; init; }
    public required string RequestedBy { get; init; }
    public DateTimeOffset RequestedAt { get; init; }
    public string? Response { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }

    public bool IsOpen => Response is null;
}
