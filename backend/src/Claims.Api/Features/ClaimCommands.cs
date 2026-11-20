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
            req.Type!.Value, req.PolicyNumber.ToUpperInvariant(), req.IncidentDate, req.IncidentLocation, req.Description,
            req.ClaimedAmount, req.VehicleRegistration, req.PropertyAddress), Now);

        repo.Add(claim);
        await repo.SaveAsync(claim, ct);
        return claim;
    }

    public async Task PickUpAsync(Guid id, string handlerId, CancellationToken ct)
    {
        var handler = await repo.GetUserAsync(handlerId, ct);
        await Execute(id, c => c.PickUp(handler, Now), ct);
    }

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

        void MaxLength(string field, string? value, int max)
        {
            if (!errors.ContainsKey(field) && value is not null && value.Trim().Length > max)
                errors[field] = [$"Please keep this under {max} characters."];
        }

        Required("marketCode", r.MarketCode);
        Required("policyNumber", r.PolicyNumber);
        Required("incidentLocation", r.IncidentLocation);
        Required("description", r.Description);
        if (r.Type is null) errors["type"] = ["Choose Motor or Property."];

        // Type-specific fields, reported together with everything else instead of one at a time.
        if (r.Type == ClaimType.Motor) Required("vehicleRegistration", r.VehicleRegistration);
        if (r.Type == ClaimType.Property) Required("propertyAddress", r.PropertyAddress);

        // C-2: limits match the column sizes, so oversized input is a 400 on the field, never a database 500.
        MaxLength("incidentLocation", r.IncidentLocation, 200);
        MaxLength("description", r.Description, 4000);
        MaxLength("vehicleRegistration", r.VehicleRegistration, 20);
        MaxLength("propertyAddress", r.PropertyAddress, 300);

        if (!errors.ContainsKey("policyNumber") && !PolicyNumberFormat().IsMatch(r.PolicyNumber))
            errors["policyNumber"] = ["Policy number must be 6–40 letters, digits or dashes."];

        if (!errors.ContainsKey("description") && r.Description.Trim().Length < 20)
            errors["description"] = ["Please describe what happened in at least 20 characters."];

        if (r.ClaimedAmount <= 0)
            errors["claimedAmount"] = ["Claimed amount must be greater than zero."];
        else if (r.ClaimedAmount > Money.MaxAmount)
            errors["claimedAmount"] = ["Amount is too large."];

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
