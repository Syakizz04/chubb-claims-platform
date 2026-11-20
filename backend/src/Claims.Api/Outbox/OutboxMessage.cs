namespace Claims.Api.Outbox;

/// <summary>
/// Transactional outbox row (E-2). Written in the same database transaction as the claim change,
/// so an event can never be lost (written to the database but never published) or invented
/// (published for a change that was rolled back).
/// </summary>
public class OutboxMessage
{
    /// <summary>Identity column: gives a strict insert order for publishing.</summary>
    public long Id { get; init; }
    public Guid EventId { get; init; }
    public required string Topic { get; init; }
    public required string Key { get; init; }       // claim id: keeps one claim's events in order on one partition
    public required string EventType { get; init; }
    public required string Payload { get; init; }   // JSON ClaimEventMessage
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
