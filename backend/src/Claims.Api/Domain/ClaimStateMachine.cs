namespace Claims.Api.Domain;

/// <summary>
/// The single source of truth for which status transitions are legal (DR-5).
/// Anything not in this table is illegal and results in a 409.
/// </summary>
public static class ClaimStateMachine
{
    private static readonly Dictionary<(ClaimStatus From, ClaimAction Action), ClaimStatus> Transitions = new()
    {
        [(ClaimStatus.Submitted, ClaimAction.PickUp)] = ClaimStatus.UnderReview,
        [(ClaimStatus.UnderReview, ClaimAction.RequestInfo)] = ClaimStatus.AwaitingInfo,
        [(ClaimStatus.AwaitingInfo, ClaimAction.ProvideInfo)] = ClaimStatus.UnderReview,
        [(ClaimStatus.UnderReview, ClaimAction.Approve)] = ClaimStatus.Approved,
        [(ClaimStatus.UnderReview, ClaimAction.Reject)] = ClaimStatus.Rejected,
        // Allowed so a claim can be closed when the claimant never responds; otherwise it would be stuck.
        [(ClaimStatus.AwaitingInfo, ClaimAction.Reject)] = ClaimStatus.Rejected,
        [(ClaimStatus.Approved, ClaimAction.Settle)] = ClaimStatus.Settled,
    };

    public static IReadOnlyCollection<(ClaimStatus From, ClaimAction Action)> LegalTransitions => Transitions.Keys;

    public static bool CanApply(ClaimStatus from, ClaimAction action) => Transitions.ContainsKey((from, action));

    /// <exception cref="InvalidTransitionException">When the transition is not in the table.</exception>
    public static ClaimStatus Apply(ClaimStatus from, ClaimAction action) =>
        Transitions.TryGetValue((from, action), out var to)
            ? to
            : throw new InvalidTransitionException(from, action);

    public static bool IsTerminal(ClaimStatus status) => status is ClaimStatus.Settled or ClaimStatus.Rejected;

    public static bool IsOpen(ClaimStatus status) => !IsTerminal(status);
}
