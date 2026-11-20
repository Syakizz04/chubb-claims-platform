using Claims.Api.Domain;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Claims.Tests.Domain;

/// <summary>
/// Property-based tests (FsCheck, the .NET counterpart to Python's Hypothesis). Instead of
/// hand-picked examples, FsCheck generates hundreds of random inputs and shrinks any failure down to
/// a minimal counter-example.
/// </summary>
public class ClaimPropertyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Market Sg = new() { Code = "SG", Name = "Singapore", CurrencyCode = "SGD", MinorUnits = 2 };
    private static readonly Market Kr = new() { Code = "KR", Name = "South Korea", CurrencyCode = "KRW", MinorUnits = 0 };

    // ------------------------------------------------------------------ money (DR-3)

    [Property(MaxTest = 500)]
    public bool Amounts_within_currency_precision_are_always_valid(PositiveInt minor, bool krw)
    {
        var minorUnits = krw ? 0 : 2;
        var amount = minor.Get / (decimal)Math.Pow(10, minorUnits);
        return Money.HasValidPrecision(amount, minorUnits);
    }

    [Property(MaxTest = 500)]
    public bool One_extra_decimal_place_is_always_invalid(PositiveInt minor, bool krw)
    {
        var minorUnits = krw ? 0 : 2;
        // e.g. 12345 → 1234.5 for KRW, or 123.45 → 12.345 for SGD, when the last digit isn't 0
        var amount = minor.Get / (decimal)Math.Pow(10, minorUnits + 1);
        return (minor.Get % 10 == 0) || !Money.HasValidPrecision(amount, minorUnits);
    }

    // ------------------------------------------------------------------ lifecycle invariants

    /// <summary>One random step: which action, by whom, with what amount.</summary>
    public record Step(int Action, int Actor, decimal Amount);

    private static readonly string[] Actors = ["handler-1", "handler-2", "claimant-sg", "claimant-kr"];

    private static Arbitrary<Step[]> Steps()
    {
        var step =
            from action in Gen.Choose(0, 7)
            from actor in Gen.Choose(0, Actors.Length - 1)
            from cents in Gen.Choose(-1_000, 600_000)
            select new Step(action, actor, cents / 100m);
        return Gen.ArrayOf(step).ToArbitrary();
    }

    private static void Apply(Claim c, Step s, DateTimeOffset at)
    {
        var who = Actors[s.Actor];
        switch (s.Action)
        {
            case 0: c.PickUp(new User { Id = who, DisplayName = who, Role = who.StartsWith("handler") ? UserRole.Handler : UserRole.Claimant }, at); break;
            case 1: c.RequestInformation(who, "Photos please", at); break;
            case 2: c.ProvideInformation(who, "Here you go", at); break;
            case 3: c.AdjustReserve(who, s.Amount, "Revised estimate", at); break;
            case 4: c.Approve(who, s.Amount, null, at); break;
            case 5: c.Reject(who, "Not covered", at); break;
            case 6: c.Settle(who, at); break;
            case 7: c.AddInternalNote(who, "Note", at); break;
        }
    }

    private record Snapshot(ClaimStatus Status, decimal Reserve, decimal? Approved, decimal? Settled,
        string? Handler, int Events, int OpenRequests);

    private static Snapshot Snap(Claim c) => new(c.Status, c.ReserveAmount, c.ApprovedAmount, c.SettledAmount,
        c.AssignedHandlerId, c.Events.Count, c.InformationRequests.Count(r => r.IsOpen));

    [Property(MaxTest = 300)]
    public Property Any_sequence_of_actions_keeps_the_claim_consistent() =>
        Prop.ForAll(Steps(), steps =>
        {
            var claim = Claim.Submit("CLM-SG-000001", Sg, "claimant-sg", new Claim.NewClaim(
                ClaimType.Motor, "POL-SG-1", new DateOnly(2026, 10, 30), "Somewhere",
                "A description long enough.", 4_000.00m, "SGX1", null), T0);

            var at = T0;
            foreach (var step in steps)
            {
                at = at.AddMinutes(1);
                var before = Snap(claim);
                var wasTerminal = ClaimStateMachine.IsTerminal(claim.Status);

                try
                {
                    Apply(claim, step, at);
                    // A successful action records exactly one event.
                    Assert.Equal(before.Events + 1, claim.Events.Count);
                    // Nothing except a note may succeed on a closed claim.
                    if (wasTerminal) Assert.Equal(7, step.Action);
                }
                catch (DomainException)
                {
                    // A rejected action must leave the claim exactly as it was (no half-applied changes).
                    Assert.Equal(before, Snap(claim));
                }

                AssertInvariants(claim);
            }
            return true;
        });

    private static void AssertInvariants(Claim c)
    {
        // Money
        Assert.True(c.ReserveAmount >= 0, "reserve is never negative");
        Assert.True(Money.HasValidPrecision(c.ReserveAmount, c.MinorUnits), "reserve respects currency precision");
        if (c.ApprovedAmount is { } approved) Assert.True(approved > 0 && approved <= c.ClaimedAmount);

        // RS-3: closed claims hold no reserve (they drop out of exposure)
        if (!c.IsOpen) Assert.Equal(0m, c.ReserveAmount);
        if (c.Status == ClaimStatus.Approved) Assert.Equal(c.ApprovedAmount, c.ReserveAmount);
        if (c.Status == ClaimStatus.Settled) Assert.Equal(c.ApprovedAmount, c.SettledAmount);

        // Assignment
        if (c.Status != ClaimStatus.Submitted) Assert.NotNull(c.AssignedHandlerId);
        if (c.AssignedHandlerId is not null) Assert.StartsWith("handler-", c.AssignedHandlerId);

        // Information loop: an open question exists exactly when the claim is awaiting info
        Assert.Equal(c.Status == ClaimStatus.AwaitingInfo, c.OpenInformationRequest is not null);

        // DR-6: the audit trail is gap-free and ordered
        Assert.Equal(Enumerable.Range(1, c.Events.Count), c.Events.Select(e => e.Sequence));
        Assert.Equal(c.Events.Count, c.LastEventSequence);

        // The audit trail replays to the current status
        var lastStatus = c.Events.Where(e => e.ToStatus is not null).Last().ToStatus;
        Assert.Equal(c.Status, lastStatus);
    }

    [Property(MaxTest = 200)]
    public Property Krw_claims_only_ever_hold_whole_won() =>
        Prop.ForAll(Gen.Choose(1, 50_000_000).ToArbitrary(), Gen.Choose(0, 50_000_000).ToArbitrary(), (claimed, reserve) =>
        {
            var c = Claim.Submit("CLM-KR-000001", Kr, "claimant-kr", new Claim.NewClaim(ClaimType.Motor, "POL-KR-1",
                new DateOnly(2026, 10, 30), "Seoul", "A description long enough.", claimed, "12A3456", null), T0);
            c.PickUp(new User { Id = "handler-1", DisplayName = "H", Role = UserRole.Handler }, T0);

            Assert.Throws<DomainValidationException>(() => c.AdjustReserve("handler-1", reserve + 0.5m, "x", T0));
            c.AdjustReserve("handler-1", reserve, "x", T0);
            return c.ReserveAmount == reserve && decimal.Truncate(c.ReserveAmount) == c.ReserveAmount;
        });
}
