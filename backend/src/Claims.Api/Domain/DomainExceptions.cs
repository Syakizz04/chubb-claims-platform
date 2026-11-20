namespace Claims.Api.Domain;

/// <summary>Base type for business-rule failures. Mapped to ProblemDetails by the API (N-5).</summary>
public abstract class DomainException(string message) : Exception(message);

/// <summary>400: the input breaks a business rule.</summary>
public sealed class DomainValidationException(string field, string message) : DomainException(message)
{
    public string Field { get; } = field;
}

/// <summary>409: the action is not legal from the claim's current status (DR-5).</summary>
public sealed class InvalidTransitionException(ClaimStatus current, ClaimAction action)
    : DomainException($"Cannot {action} a claim that is {current}.")
{
    public ClaimStatus Current { get; } = current;
    public ClaimAction Action { get; } = action;
}

/// <summary>403: the user is not allowed to act on this claim (H-4).</summary>
public sealed class ForbiddenException(string message) : DomainException(message);

/// <summary>404: not found, or not visible to this user (C-3).</summary>
public sealed class NotFoundException(string message) : DomainException(message);

/// <summary>409: the action conflicts with the claim's current state (not a status transition).</summary>
public sealed class ConflictException(string message) : DomainException(message);
