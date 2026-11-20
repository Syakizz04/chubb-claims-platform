namespace Claims.Api.Domain;

/// <summary>
/// The claim aggregate. All state changes go through methods on this class, so the business
/// rules live in one place and can be unit tested without a database or HTTP.
/// Each method emits exactly one <see cref="ClaimEvent"/>.
/// </summary>
public class Claim
{
    private readonly List<ClaimEvent> _events = [];
    private readonly List<InformationRequest> _informationRequests = [];
    private readonly List<ClaimEvent> _uncommitted = [];

    private Claim() { } // EF Core

    public Guid Id { get; private set; }
    public string Reference { get; private set; } = default!;
    public string MarketCode { get; private set; } = default!;
    public string CurrencyCode { get; private set; } = default!;
    public int MinorUnits { get; private set; }
    public ClaimType Type { get; private set; }
    public string PolicyNumber { get; private set; } = default!;
    public DateOnly IncidentDate { get; private set; }
    public string IncidentLocation { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public string? VehicleRegistration { get; private set; }
    public string? PropertyAddress { get; private set; }

    public decimal ClaimedAmount { get; private set; }
    public decimal ReserveAmount { get; private set; }
    public decimal? ApprovedAmount { get; private set; }
    public decimal? SettledAmount { get; private set; }
    public string? RejectionReason { get; private set; }

    public ClaimStatus Status { get; private set; }
    public string ClaimantId { get; private set; } = default!;
    public string? AssignedHandlerId { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Number of events so far; the next event gets LastEventSequence + 1.</summary>
    public int LastEventSequence { get; private set; }

    /// <summary>Optimistic concurrency token (DR-7). Mapped to Postgres xmin.</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<ClaimEvent> Events => _events;
    public IReadOnlyList<InformationRequest> InformationRequests => _informationRequests;

    /// <summary>Events raised since the claim was loaded, which still need to go to the outbox.</summary>
    public IReadOnlyList<ClaimEvent> UncommittedEvents => _uncommitted;
    public void MarkEventsCommitted() => _uncommitted.Clear();

    public bool IsOpen => ClaimStateMachine.IsOpen(Status);
    public InformationRequest? OpenInformationRequest => _informationRequests.FirstOrDefault(r => r.IsOpen);

    public record NewClaim(
        ClaimType Type,
        string PolicyNumber,
        DateOnly IncidentDate,
        string IncidentLocation,
        string Description,
        decimal ClaimedAmount,
        string? VehicleRegistration,
        string? PropertyAddress);

    // ---------------------------------------------------------------- C-1: submit

    public static Claim Submit(string reference, Market market, string claimantId, NewClaim input, DateTimeOffset now)
    {
        Money.EnsureValid(nameof(input.ClaimedAmount), input.ClaimedAmount, market.MinorUnits);

        if (input.IncidentDate > DateOnly.FromDateTime(now.UtcDateTime))
            throw new DomainValidationException(nameof(input.IncidentDate), "Incident date cannot be in the future.");

        if (input.Type == ClaimType.Motor && string.IsNullOrWhiteSpace(input.VehicleRegistration))
            throw new DomainValidationException(nameof(input.VehicleRegistration), "Vehicle registration is required for motor claims.");

        if (input.Type == ClaimType.Property && string.IsNullOrWhiteSpace(input.PropertyAddress))
            throw new DomainValidationException(nameof(input.PropertyAddress), "Property address is required for property claims.");

        var claim = new Claim
        {
            Id = Guid.NewGuid(),
            Reference = reference,
            MarketCode = market.Code,
            CurrencyCode = market.CurrencyCode, // DR-2: derived from market, never client-supplied
            MinorUnits = market.MinorUnits,
            Type = input.Type,
            PolicyNumber = input.PolicyNumber.Trim(),
            IncidentDate = input.IncidentDate,
            IncidentLocation = input.IncidentLocation.Trim(),
            Description = input.Description.Trim(),
            VehicleRegistration = input.Type == ClaimType.Motor ? input.VehicleRegistration?.Trim() : null,
            PropertyAddress = input.Type == ClaimType.Property ? input.PropertyAddress?.Trim() : null,
            ClaimedAmount = input.ClaimedAmount,
            ReserveAmount = input.ClaimedAmount, // RS-1: initial reserve = claimed amount
            Status = ClaimStatus.Submitted,
            ClaimantId = claimantId,
            SubmittedAt = now,
        };

        claim.Raise(ClaimEventType.Submitted, claimantId, now, from: null, to: ClaimStatus.Submitted, amount: input.ClaimedAmount);
        return claim;
    }

    // ---------------------------------------------------------------- H-2: pick up

    public void PickUp(User handler, DateTimeOffset now)
    {
        // Enforced here, not only by the HTTP policy, so no caller (job, script, future endpoint)
        // can assign a claim to a non-handler. Found by a property-based test.
        if (handler.Role != UserRole.Handler)
            throw new ForbiddenException("Only claims handlers can pick up claims.");

        var from = Status;
        Status = ClaimStateMachine.Apply(Status, ClaimAction.PickUp);
        AssignedHandlerId = handler.Id;
        Raise(ClaimEventType.PickedUp, handler.Id, now, from, Status);
    }

    // ---------------------------------------------------------------- H-5 / C-5: information loop

    public void RequestInformation(string handlerId, string question, DateTimeOffset now)
    {
        EnsureAssignedTo(handlerId);
        RequireText(nameof(question), question);

        var from = Status;
        Status = ClaimStateMachine.Apply(Status, ClaimAction.RequestInfo);
        _informationRequests.Add(new InformationRequest
        {
            ClaimId = Id,
            Question = question.Trim(),
            RequestedBy = handlerId,
            RequestedAt = now,
        });
        Raise(ClaimEventType.InformationRequested, handlerId, now, from, Status, note: question.Trim());
    }

    public void ProvideInformation(string claimantId, string response, DateTimeOffset now)
    {
        if (claimantId != ClaimantId)
            throw new NotFoundException("Claim not found."); // C-3: don't reveal other people's claims

        RequireText(nameof(response), response);

        var from = Status;
        Status = ClaimStateMachine.Apply(Status, ClaimAction.ProvideInfo);

        var request = OpenInformationRequest
                      ?? throw new ConflictException("There is no open information request on this claim.");
        request.Response = response.Trim();
        request.RespondedAt = now;

        Raise(ClaimEventType.InformationProvided, claimantId, now, from, Status, note: response.Trim());
    }

    // ---------------------------------------------------------------- RS-2: reserves

    public void AdjustReserve(string handlerId, decimal newReserve, string reason, DateTimeOffset now)
    {
        EnsureAssignedTo(handlerId);
        RequireText(nameof(reason), reason);

        if (Status is not (ClaimStatus.UnderReview or ClaimStatus.AwaitingInfo))
            throw new ConflictException($"The reserve can only be adjusted while a claim is being assessed (it is {Status}).");

        Money.EnsureValid(nameof(newReserve), newReserve, MinorUnits, allowZero: true);

        var previous = ReserveAmount;
        ReserveAmount = newReserve;
        Raise(ClaimEventType.ReserveAdjusted, handlerId, now, Status, Status,
            note: reason.Trim(), amount: newReserve, previousAmount: previous, isInternal: true);
    }

    // ---------------------------------------------------------------- H-7 / H-8 / H-9: decisions

    public void Approve(string handlerId, decimal approvedAmount, string? note, DateTimeOffset now)
    {
        EnsureAssignedTo(handlerId);
        Money.EnsureValid(nameof(approvedAmount), approvedAmount, MinorUnits);
        if (approvedAmount > ClaimedAmount)
            throw new DomainValidationException(nameof(approvedAmount), "Approved amount cannot exceed the claimed amount.");

        var from = Status;
        Status = ClaimStateMachine.Apply(Status, ClaimAction.Approve);
        var previousReserve = ReserveAmount;
        ApprovedAmount = approvedAmount;
        ReserveAmount = approvedAmount; // RS-3
        DecidedAt = now;
        Raise(ClaimEventType.Approved, handlerId, now, from, Status,
            note: note?.Trim(), amount: approvedAmount, previousAmount: previousReserve);
    }

    public void Reject(string handlerId, string reason, DateTimeOffset now)
    {
        EnsureAssignedTo(handlerId);
        RequireText(nameof(reason), reason);

        var from = Status;
        Status = ClaimStateMachine.Apply(Status, ClaimAction.Reject);
        var previousReserve = ReserveAmount;
        RejectionReason = reason.Trim();
        ReserveAmount = 0; // RS-3: reserve released
        DecidedAt = now;
        ClosedAt = now;
        if (OpenInformationRequest is { } unanswered)
        {
            unanswered.Response = "(closed without a response)";
            unanswered.RespondedAt = now;
        }
        Raise(ClaimEventType.Rejected, handlerId, now, from, Status,
            note: reason.Trim(), amount: 0, previousAmount: previousReserve);
    }

    public void Settle(string handlerId, DateTimeOffset now)
    {
        EnsureAssignedTo(handlerId);

        var from = Status;
        Status = ClaimStateMachine.Apply(Status, ClaimAction.Settle);
        var previousReserve = ReserveAmount;
        SettledAmount = ApprovedAmount;
        ReserveAmount = 0; // RS-3: reserve released
        ClosedAt = now;
        Raise(ClaimEventType.Settled, handlerId, now, from, Status,
            amount: SettledAmount, previousAmount: previousReserve);
    }

    // ---------------------------------------------------------------- H-10: notes

    public void AddInternalNote(string staffId, string text, DateTimeOffset now)
    {
        RequireText(nameof(text), text);
        Raise(ClaimEventType.NoteAdded, staffId, now, Status, Status, note: text.Trim(), isInternal: true);
    }

    // ---------------------------------------------------------------- helpers

    private void EnsureAssignedTo(string handlerId)
    {
        if (AssignedHandlerId != handlerId)
            throw new ForbiddenException("Only the handler assigned to this claim can do that.");
    }

    private static void RequireText(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainValidationException(field, "This field is required.");
    }

    private void Raise(ClaimEventType type, string actorId, DateTimeOffset now, ClaimStatus? from, ClaimStatus? to,
        string? note = null, decimal? amount = null, decimal? previousAmount = null, bool isInternal = false)
    {
        LastEventSequence++;
        UpdatedAt = now;
        var e = new ClaimEvent
        {
            ClaimId = Id,
            Sequence = LastEventSequence,
            Type = type,
            FromStatus = from,
            ToStatus = to,
            ActorId = actorId,
            Note = note,
            Amount = amount,
            PreviousAmount = previousAmount,
            IsInternal = isInternal,
            OccurredAt = now,
        };
        _events.Add(e);
        _uncommitted.Add(e);
    }
}
