namespace Claims.Api.Domain;

public enum ClaimType
{
    Motor,
    Property
}

public enum ClaimStatus
{
    Submitted,
    UnderReview,
    AwaitingInfo,
    Approved,
    Rejected,
    Settled
}

/// <summary>Commands that move a claim between statuses (see <see cref="ClaimStateMachine"/>).</summary>
public enum ClaimAction
{
    PickUp,
    RequestInfo,
    ProvideInfo,
    Approve,
    Reject,
    Settle
}

public enum ClaimEventType
{
    Submitted,
    PickedUp,
    InformationRequested,
    InformationProvided,
    ReserveAdjusted,
    Approved,
    Rejected,
    Settled,
    NoteAdded
}

public enum UserRole
{
    Claimant,
    Handler,
    Manager
}
