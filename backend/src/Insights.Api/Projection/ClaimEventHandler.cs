using Claims.Contracts;
using Insights.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Insights.Api.Projection;

/// <summary>
/// Applies one Kafka event to the read model. The dedupe record, snapshot update and notification
/// are all saved in one transaction, so a redelivered event (at-least-once) is a no-op (E-3).
/// </summary>
public class ClaimEventHandler(InsightsDbContext db, TimeProvider clock, ILogger<ClaimEventHandler> log)
{
    public enum Outcome { Applied, Duplicate, Stale }

    public async Task<Outcome> HandleAsync(ClaimEventMessage e, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (await db.ProcessedEvents.AnyAsync(p => p.EventId == e.EventId, ct))
        {
            log.LogDebug("Duplicate event {EventId} for {Reference} ignored", e.EventId, e.Reference);
            return Outcome.Duplicate;
        }

        db.ProcessedEvents.Add(new ProcessedEvent { EventId = e.EventId, ProcessedAt = clock.GetUtcNow(), OccurredAt = e.OccurredAt });

        var snapshot = await db.Claims.FindAsync([e.ClaimId], ct);
        if (snapshot is null)
        {
            snapshot = new ClaimSnapshot { ClaimId = e.ClaimId };
            db.Claims.Add(snapshot);
        }
        var changed = ClaimProjection.Apply(snapshot, e);

        if (ClaimProjection.NotificationFor(e) is { } message)
        {
            db.Notifications.Add(new Notification
            {
                EventId = e.EventId, ClaimId = e.ClaimId, ClaimantId = e.ClaimantId,
                Reference = e.Reference, Message = message, CreatedAt = e.OccurredAt,
            });
            log.LogInformation("Notify {ClaimantId}: {Message}", e.ClaimantId, message);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        log.LogInformation("Projected {EventType} #{Sequence} for {Reference} (event {EventId})",
            e.EventType, e.Sequence, e.Reference, e.EventId);
        return changed ? Outcome.Applied : Outcome.Stale;
    }
}
