using Claims.Api.Domain;

namespace Claims.Api.Features;

// Separate claimant and staff view models on purpose: the claimant DTOs have no reserve or
// internal-note fields at all, so they can't leak by accident (C-4).

// ------------------------------------------------------------------ requests

public record SubmitClaimRequest(
    string MarketCode,
    ClaimType Type,
    string PolicyNumber,
    DateOnly IncidentDate,
    string IncidentLocation,
    string Description,
    decimal ClaimedAmount,
    string? VehicleRegistration,
    string? PropertyAddress);

public record InformationResponseRequest(string Response);
public record InformationRequestRequest(string Question);
public record AdjustReserveRequest(decimal Amount, string Reason);
public record ApproveRequest(decimal Amount, string? Note);
public record RejectRequest(string Reason);
public record AddNoteRequest(string Text);

// ------------------------------------------------------------------ shared

public record MarketDto(string Code, string Name, string CurrencyCode, int MinorUnits);
public record UserDto(string Id, string DisplayName, UserRole Role);

public record TimelineEntryDto(
    int Sequence,
    ClaimEventType Type,
    ClaimStatus? FromStatus,
    ClaimStatus? ToStatus,
    string ActorName,
    string? Note,
    decimal? Amount,
    decimal? PreviousAmount,
    bool IsInternal,
    DateTimeOffset OccurredAt);

public record InformationRequestDto(
    Guid Id,
    string Question,
    DateTimeOffset RequestedAt,
    string? Response,
    DateTimeOffset? RespondedAt);

// ------------------------------------------------------------------ claimant views

public record ClaimantClaimSummary(
    Guid Id,
    string Reference,
    string MarketCode,
    string CurrencyCode,
    ClaimType Type,
    ClaimStatus Status,
    decimal ClaimedAmount,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt,
    bool ActionRequired);

public record ClaimantClaimDetail(
    Guid Id,
    string Reference,
    string MarketCode,
    string CurrencyCode,
    int MinorUnits,
    ClaimType Type,
    ClaimStatus Status,
    string PolicyNumber,
    DateOnly IncidentDate,
    string IncidentLocation,
    string Description,
    string? VehicleRegistration,
    string? PropertyAddress,
    decimal ClaimedAmount,
    decimal? ApprovedAmount,
    decimal? SettledAmount,
    string? RejectionReason,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? ClosedAt,
    InformationRequestDto? OpenInformationRequest,
    IReadOnlyList<InformationRequestDto> InformationRequests,
    IReadOnlyList<TimelineEntryDto> Timeline);

// ------------------------------------------------------------------ staff views

public record StaffClaimSummary(
    Guid Id,
    string Reference,
    string MarketCode,
    string CurrencyCode,
    ClaimType Type,
    ClaimStatus Status,
    string ClaimantName,
    string? AssignedHandlerId,
    string? AssignedHandlerName,
    decimal ClaimedAmount,
    decimal ReserveAmount,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt,
    int AgeDays);

public record StaffClaimDetail(
    Guid Id,
    string Reference,
    string MarketCode,
    string CurrencyCode,
    int MinorUnits,
    ClaimType Type,
    ClaimStatus Status,
    string PolicyNumber,
    DateOnly IncidentDate,
    string IncidentLocation,
    string Description,
    string? VehicleRegistration,
    string? PropertyAddress,
    string ClaimantId,
    string ClaimantName,
    string? AssignedHandlerId,
    string? AssignedHandlerName,
    decimal ClaimedAmount,
    decimal ReserveAmount,
    decimal? ApprovedAmount,
    decimal? SettledAmount,
    string? RejectionReason,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? ClosedAt,
    int AgeDays,
    IReadOnlyList<string> AvailableActions, // what the current user may do now; the UI shows buttons from this
    IReadOnlyList<InformationRequestDto> InformationRequests,
    IReadOnlyList<TimelineEntryDto> Timeline);
