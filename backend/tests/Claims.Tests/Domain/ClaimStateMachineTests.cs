using Claims.Api.Domain;

namespace Claims.Tests.Domain;

/// <summary>DR-5: every (status, action) pair is either explicitly legal or rejected.</summary>
public class ClaimStateMachineTests
{
    public static TheoryData<ClaimStatus, ClaimAction, ClaimStatus> Legal => new()
    {
        { ClaimStatus.Submitted, ClaimAction.PickUp, ClaimStatus.UnderReview },
        { ClaimStatus.UnderReview, ClaimAction.RequestInfo, ClaimStatus.AwaitingInfo },
        { ClaimStatus.AwaitingInfo, ClaimAction.ProvideInfo, ClaimStatus.UnderReview },
        { ClaimStatus.UnderReview, ClaimAction.Approve, ClaimStatus.Approved },
        { ClaimStatus.UnderReview, ClaimAction.Reject, ClaimStatus.Rejected },
        { ClaimStatus.AwaitingInfo, ClaimAction.Reject, ClaimStatus.Rejected },
        { ClaimStatus.Approved, ClaimAction.Settle, ClaimStatus.Settled },
    };

    [Theory]
    [MemberData(nameof(Legal))]
    public void Legal_transitions_move_to_the_expected_status(ClaimStatus from, ClaimAction action, ClaimStatus expected)
    {
        Assert.Equal(expected, ClaimStateMachine.Apply(from, action));
    }

    public static TheoryData<ClaimStatus, ClaimAction> Illegal()
    {
        var legal = ClaimStateMachine.LegalTransitions.ToHashSet();
        var data = new TheoryData<ClaimStatus, ClaimAction>();
        foreach (var status in Enum.GetValues<ClaimStatus>())
        foreach (var action in Enum.GetValues<ClaimAction>())
            if (!legal.Contains((status, action)))
                data.Add(status, action);
        return data;
    }

    [Theory]
    [MemberData(nameof(Illegal))]
    public void Every_other_transition_is_rejected(ClaimStatus from, ClaimAction action)
    {
        var ex = Assert.Throws<InvalidTransitionException>(() => ClaimStateMachine.Apply(from, action));
        Assert.Equal(from, ex.Current);
    }

    [Fact]
    public void The_legal_table_is_exactly_the_documented_one()
    {
        // Guards against someone quietly adding a transition without updating the docs and tests.
        Assert.Equal(Legal.Count, ClaimStateMachine.LegalTransitions.Count);
    }

    [Theory]
    [InlineData(ClaimStatus.Settled)]
    [InlineData(ClaimStatus.Rejected)]
    public void Terminal_statuses_allow_no_actions(ClaimStatus terminal)
    {
        Assert.True(ClaimStateMachine.IsTerminal(terminal));
        Assert.All(Enum.GetValues<ClaimAction>(), a => Assert.False(ClaimStateMachine.CanApply(terminal, a)));
    }
}
