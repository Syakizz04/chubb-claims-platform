using System.Text.Json;
using Claims.Api.Domain;
using Claims.Api.Outbox;
using Claims.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Data;

/// <summary>
/// Loads and saves claim aggregates. Saving writes the claim change, its audit events and the
/// matching outbox rows in a single SaveChanges, which means a single transaction (E-2).
/// </summary>
public class ClaimRepository(ClaimsDbContext db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Claim> GetAsync(Guid id, CancellationToken ct) =>
        await db.Claims
            .Include(c => c.Events)
            .Include(c => c.InformationRequests)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw new NotFoundException("Claim not found.");

    public async Task<Market> GetMarketAsync(string code, CancellationToken ct) =>
        await db.Markets.FindAsync([code.ToUpperInvariant()], ct)
        ?? throw new DomainValidationException("marketCode", $"Unknown market '{code}'.");

    /// <summary>
    /// Atomically allocates the next reference number for a market (DR-4). A single UPDATE … RETURNING
    /// is safe under concurrency without application locks. A rolled-back submission leaves a gap,
    /// which is acceptable for references (just as with database sequences).
    /// </summary>
    public async Task<string> NextReferenceAsync(string marketCode, CancellationToken ct)
    {
        var next = await db.Database
            .SqlQuery<long>($"""
                UPDATE "MarketSequences" SET "LastValue" = "LastValue" + 1
                WHERE "MarketCode" = {marketCode}
                RETURNING "LastValue" AS "Value"
                """)
            .ToListAsync(ct);
        return $"CLM-{marketCode}-{next.Single():D6}";
    }

    public async Task<User> GetUserAsync(string id, CancellationToken ct) =>
        await db.Users.FindAsync([id], ct) ?? throw new ForbiddenException($"Unknown user '{id}'.");

    public void Add(Claim claim) => db.Claims.Add(claim);

    /// <summary>Persists the claim, its new events and their outbox rows atomically.</summary>
    public async Task SaveAsync(Claim claim, CancellationToken ct)
    {
        // The outbox snapshot is the claim's state after the event, so only one event per save is correct.
        if (claim.UncommittedEvents.Count > 1)
            throw new InvalidOperationException("Save the claim after each action: one event per transaction.");

        string? handlerName = null;
        if (claim.AssignedHandlerId is not null)
            handlerName = await db.Users.Where(u => u.Id == claim.AssignedHandlerId)
                .Select(u => u.DisplayName).FirstOrDefaultAsync(ct);

        foreach (var e in claim.UncommittedEvents)
        {
            // Navigations on a tracked aggregate don't always detect new children added through a
            // backing field, so mark new events and requests as Added explicitly.
            if (db.Entry(e).State == EntityState.Detached) db.ClaimEvents.Add(e);

            var message = ToMessage(claim, e, handlerName);
            db.Outbox.Add(new OutboxMessage
            {
                EventId = e.Id,
                Topic = Topics.ClaimEvents,
                Key = claim.Id.ToString(),
                EventType = e.Type.ToString(),
                Payload = JsonSerializer.Serialize(message, Json),
                CreatedAt = e.OccurredAt,
            });
        }

        foreach (var r in claim.InformationRequests)
            if (db.Entry(r).State == EntityState.Detached) db.InformationRequests.Add(r);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // DR-7 / H-2: someone else changed this claim between our read and our write.
            throw new ConflictException("This claim was changed by someone else. Refresh and try again.");
        }

        claim.MarkEventsCommitted();
    }

    /// <summary>
    /// Builds the integration event. The snapshot reflects the claim's state after this event; we
    /// only ever raise one event per save, so the current state is correct.
    /// </summary>
    private static ClaimEventMessage ToMessage(Claim c, ClaimEvent e, string? handlerName) => new()
    {
        EventId = e.Id,
        EventType = e.Type.ToString(),
        OccurredAt = e.OccurredAt,
        ActorId = e.ActorId,
        ClaimId = c.Id,
        Sequence = e.Sequence,
        Reference = c.Reference,
        MarketCode = c.MarketCode,
        CurrencyCode = c.CurrencyCode,
        CurrencyMinorUnits = c.MinorUnits,
        ClaimType = c.Type.ToString(),
        Status = (e.ToStatus ?? c.Status).ToString(),
        PreviousStatus = e.FromStatus?.ToString(),
        ClaimantId = c.ClaimantId,
        HandlerId = c.AssignedHandlerId,
        HandlerName = handlerName,
        ClaimedAmount = c.ClaimedAmount,
        ReserveAmount = c.ReserveAmount,
        ApprovedAmount = c.ApprovedAmount,
        SettledAmount = c.SettledAmount,
        SubmittedAt = c.SubmittedAt,
        DecidedAt = c.DecidedAt,
        ClosedAt = c.ClosedAt,
        PublicNote = e.IsInternal ? null : e.Note,
    };
}
