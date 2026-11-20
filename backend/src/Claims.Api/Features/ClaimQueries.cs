using Claims.Api.Data;
using Claims.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Features;

/// <summary>Read-side queries. No tracking, projected straight to DTOs.</summary>
public class ClaimQueries(ClaimsDbContext db, TimeProvider clock)
{
    private DateTimeOffset Now => clock.GetUtcNow();

    // ------------------------------------------------------------ claimant

    public async Task<List<ClaimantClaimSummary>> ForClaimantAsync(string claimantId, CancellationToken ct) =>
        await db.Claims.AsNoTracking()
            .Where(c => c.ClaimantId == claimantId)
            .OrderByDescending(c => c.SubmittedAt)
            .Select(c => new ClaimantClaimSummary(c.Id, c.Reference, c.MarketCode, c.CurrencyCode, c.Type, c.Status,
                c.ClaimedAmount, c.SubmittedAt, c.UpdatedAt, c.Status == ClaimStatus.AwaitingInfo))
            .ToListAsync(ct);

    public async Task<ClaimantClaimDetail> ClaimantDetailAsync(string claimantId, Guid id, CancellationToken ct)
    {
        var c = await LoadAsync(id, ct);
        if (c.ClaimantId != claimantId) throw new NotFoundException("Claim not found."); // C-3

        var names = await NamesAsync(c, ct);
        var requests = c.InformationRequests.OrderBy(r => r.RequestedAt).Select(ToDto).ToList();
        var timeline = c.Events.Where(e => !e.IsInternal).OrderBy(e => e.Sequence)
            .Select(e => ToTimeline(e, names, hideAmountsFor: claimantId)).ToList();

        return new ClaimantClaimDetail(c.Id, c.Reference, c.MarketCode, c.CurrencyCode, c.MinorUnits, c.Type, c.Status,
            c.PolicyNumber, c.IncidentDate, c.IncidentLocation, c.Description, c.VehicleRegistration, c.PropertyAddress,
            c.ClaimedAmount, c.ApprovedAmount, c.SettledAmount, c.RejectionReason,
            c.SubmittedAt, c.UpdatedAt, c.DecidedAt, c.ClosedAt,
            c.OpenInformationRequest is { } open ? ToDto(open) : null, requests, timeline);
    }

    // ------------------------------------------------------------ staff

    /// <summary>H-1: unassigned submitted claims, oldest first.</summary>
    public Task<List<StaffClaimSummary>> QueueAsync(string? market, ClaimType? type, CancellationToken ct) =>
        StaffSummaries(db.Claims.Where(c => c.Status == ClaimStatus.Submitted && c.AssignedHandlerId == null)
            .Where(c => market == null || c.MarketCode == market.ToUpper())
            .Where(c => type == null || c.Type == type)
            .OrderBy(c => c.SubmittedAt), ct);

    /// <summary>H-3: my open claims, oldest first.</summary>
    public Task<List<StaffClaimSummary>> AssignedToAsync(string handlerId, CancellationToken ct) =>
        StaffSummaries(db.Claims
            .Where(c => c.AssignedHandlerId == handlerId
                        && c.Status != ClaimStatus.Settled && c.Status != ClaimStatus.Rejected)
            .OrderBy(c => c.SubmittedAt), ct);

    /// <summary>All claims, for search and for managers. Newest first.</summary>
    public Task<List<StaffClaimSummary>> SearchAsync(ClaimStatus? status, string? market, CancellationToken ct) =>
        StaffSummaries(db.Claims
            .Where(c => status == null || c.Status == status)
            .Where(c => market == null || c.MarketCode == market.ToUpper())
            .OrderByDescending(c => c.SubmittedAt)
            .Take(200), ct);

    public async Task<StaffClaimDetail> StaffDetailAsync(string userId, bool isHandler, Guid id, CancellationToken ct)
    {
        var c = await LoadAsync(id, ct);
        var names = await NamesAsync(c, ct);
        var timeline = c.Events.OrderBy(e => e.Sequence).Select(e => ToTimeline(e, names, hideAmountsFor: null)).ToList();

        return new StaffClaimDetail(c.Id, c.Reference, c.MarketCode, c.CurrencyCode, c.MinorUnits, c.Type, c.Status,
            c.PolicyNumber, c.IncidentDate, c.IncidentLocation, c.Description, c.VehicleRegistration, c.PropertyAddress,
            c.ClaimantId, names.GetValueOrDefault(c.ClaimantId, c.ClaimantId),
            c.AssignedHandlerId, c.AssignedHandlerId is null ? null : names.GetValueOrDefault(c.AssignedHandlerId),
            c.ClaimedAmount, c.ReserveAmount, c.ApprovedAmount, c.SettledAmount, c.RejectionReason,
            c.SubmittedAt, c.UpdatedAt, c.DecidedAt, c.ClosedAt, AgeDays(c.SubmittedAt),
            AvailableActions(c, userId, isHandler), c.InformationRequests.OrderBy(r => r.RequestedAt).Select(ToDto).ToList(),
            timeline);
    }

    /// <summary>
    /// The backend decides which buttons the user sees, from the same state machine that enforces
    /// the rules, so the frontend never has to duplicate the business rules.
    /// </summary>
    private static List<string> AvailableActions(Claim c, string userId, bool isHandler)
    {
        var actions = new List<string>();
        if (isHandler && ClaimStateMachine.CanApply(c.Status, ClaimAction.PickUp) && c.AssignedHandlerId is null)
            actions.Add(nameof(ClaimAction.PickUp));

        if (c.AssignedHandlerId == userId)
        {
            foreach (var a in new[] { ClaimAction.RequestInfo, ClaimAction.Approve, ClaimAction.Reject, ClaimAction.Settle })
                if (ClaimStateMachine.CanApply(c.Status, a)) actions.Add(a.ToString());
            if (c.Status is ClaimStatus.UnderReview or ClaimStatus.AwaitingInfo) actions.Add("AdjustReserve");
        }
        actions.Add("AddNote");
        return actions;
    }

    // ------------------------------------------------------------ helpers

    private int AgeDays(DateTimeOffset submittedAt) => Math.Max(0, (int)(Now - submittedAt).TotalDays);

    private async Task<List<StaffClaimSummary>> StaffSummaries(IQueryable<Claim> query, CancellationToken ct)
    {
        var rows = await query.AsNoTracking()
            .Select(c => new
            {
                Claim = c,
                ClaimantName = db.Users.Where(u => u.Id == c.ClaimantId).Select(u => u.DisplayName).FirstOrDefault(),
                HandlerName = db.Users.Where(u => u.Id == c.AssignedHandlerId).Select(u => u.DisplayName).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return rows.Select(r => new StaffClaimSummary(r.Claim.Id, r.Claim.Reference, r.Claim.MarketCode,
            r.Claim.CurrencyCode, r.Claim.Type, r.Claim.Status, r.ClaimantName ?? r.Claim.ClaimantId,
            r.Claim.AssignedHandlerId, r.HandlerName, r.Claim.ClaimedAmount, r.Claim.ReserveAmount,
            r.Claim.SubmittedAt, r.Claim.UpdatedAt, AgeDays(r.Claim.SubmittedAt))).ToList();
    }

    private async Task<Claim> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Claims.AsNoTracking()
            .Include(c => c.Events)
            .Include(c => c.InformationRequests)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw new NotFoundException("Claim not found.");

    private async Task<Dictionary<string, string>> NamesAsync(Claim c, CancellationToken ct)
    {
        var ids = c.Events.Select(e => e.ActorId).Append(c.ClaimantId).Append(c.AssignedHandlerId ?? "").Distinct().ToList();
        return await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    private static InformationRequestDto ToDto(InformationRequest r) =>
        new(r.Id, r.Question, r.RequestedAt, r.Response, r.RespondedAt);

    /// <param name="hideAmountsFor">For claimant views: hide reserve figures that ride along on decision events.</param>
    private static TimelineEntryDto ToTimeline(ClaimEvent e, Dictionary<string, string> names, string? hideAmountsFor)
    {
        var claimantView = hideAmountsFor is not null;
        var amount = e.Amount;
        decimal? previous = e.PreviousAmount;
        if (claimantView)
        {
            previous = null; // PreviousAmount on decisions is the old reserve: internal
            if (e.Type is ClaimEventType.Rejected) amount = null;
        }

        var actor = names.GetValueOrDefault(e.ActorId, e.ActorId);
        if (claimantView && e.ActorId != hideAmountsFor) actor = "Claims team"; // don't expose staff names to claimants

        return new TimelineEntryDto(e.Sequence, e.Type, e.FromStatus, e.ToStatus, actor, e.Note, amount, previous,
            e.IsInternal, e.OccurredAt);
    }
}
