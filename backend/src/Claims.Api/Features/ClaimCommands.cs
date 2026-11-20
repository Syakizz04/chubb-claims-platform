using System.Text.RegularExpressions;
using Claims.Api.Data;
using Claims.Api.Domain;

namespace Claims.Api.Features;

/// <summary>
/// Write side. Each command is load → call one domain method → save (claim, events and outbox in
/// one transaction). Business rules live in the domain; this class only coordinates.
/// </summary>
public partial class ClaimCommands(ClaimRepository repo, TimeProvider clock)
{
    private DateTimeOffset Now => clock.GetUtcNow();

    public async Task<Claim> SubmitAsync(string claimantId, SubmitClaimRequest req, CancellationToken ct)
    {
        ValidateSubmission(req);
        var market = await repo.GetMarketAsync(req.MarketCode, ct);
        var reference = await repo.NextReferenceAsync(market.Code, ct);

        var claim = Claim.Submit(reference, market, claimantId, new Claim.NewClaim(
            req.Type, req.PolicyNumber.ToUpperInvariant(), req.IncidentDate, req.IncidentLocation, req.Description,
            req.ClaimedAmount, req.VehicleRegistration, req.PropertyAddress), Now);

        repo.Add(claim);
        await repo.SaveAsync(claim, ct);
        return claim;
    }

    public Task PickUpAsync(Guid id, string handlerId, CancellationToken ct) =>
        Execute(id, c => c.PickUp(handlerId, Now), ct);

    public Task RequestInformationAsync(Guid id, string handlerId, string question, CancellationToken ct) =>
        Execute(id, c => c.RequestInformation(handlerId, question, Now), ct);

    public Task ProvideInformationAsync(Guid id, string claimantId, string response, CancellationToken ct) =>
        Execute(id, c => c.ProvideInformation(claimantId, response, Now), ct, hideFromOthers: claimantId);

    public Task AdjustReserveAsync(Guid id, string handlerId, decimal amount, string reason, CancellationToken ct) =>
        Execute(id, c => c.AdjustReserve(handlerId, amount, reason, Now), ct);

    public Task ApproveAsync(Guid id, string handlerId, decimal amount, string? note, CancellationToken ct) =>
        Execute(id, c => c.Approve(handlerId, amount, note, Now), ct);

    public Task RejectAsync(Guid id, string handlerId, string reason, CancellationToken ct) =>
        Execute(id, c => c.Reject(handlerId, reason, Now), ct);

    public Task SettleAsync(Guid id, string handlerId, CancellationToken ct) =>
        Execute(id, c => c.Settle(handlerId, Now), ct);

    public Task AddNoteAsync(Guid id, string staffId, string text, CancellationToken ct) =>
        Execute(id, c => c.AddInternalNote(staffId, text, Now), ct);

    private async Task Execute(Guid id, Action<Claim> action, CancellationToken ct, string? hideFromOthers = null)
    {
        var claim = await repo.GetAsync(id, ct);
        if (hideFromOthers is not null && claim.ClaimantId != hideFromOthers)
            throw new NotFoundException("Claim not found."); // C-3: checked before any state is revealed
        action(claim);
        await repo.SaveAsync(claim, ct);
    }

    // ------------------------------------------------------------ C-2: input validation

    [GeneratedRegex("^[A-Za-z0-9-]{6,40}$")]
    private static partial Regex PolicyNumberFormat();

    private static void ValidateSubmission(SubmitClaimRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        void Required(string field, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) errors[field] = ["This field is required."];
        }

        Required("marketCode", r.MarketCode);
        Required("policyNumber", r.PolicyNumber);
        Required("incidentLocation", r.IncidentLocation);
        Required("description", r.Description);

        if (!errors.ContainsKey("policyNumber") && !PolicyNumberFormat().IsMatch(r.PolicyNumber))
            errors["policyNumber"] = ["Policy number must be 6–40 letters, digits or dashes."];

        if (!errors.ContainsKey("description") && r.Description.Trim().Length < 20)
            errors["description"] = ["Please describe what happened in at least 20 characters."];

        if (r.ClaimedAmount <= 0)
            errors["claimedAmount"] = ["Claimed amount must be greater than zero."];

        if (r.IncidentDate == default)
            errors["incidentDate"] = ["This field is required."];

        if (errors.Count > 0) throw new RequestValidationException(errors);
    }
}

/// <summary>400 carrying several field errors at once (RFC 7807 validation problem).</summary>
public sealed class RequestValidationException(IDictionary<string, string[]> errors) : Exception("Validation failed.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}
