using Claims.Api.Domain;

namespace Claims.Tests.Domain;

public class ClaimTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly Market Sg = new() { Code = "SG", Name = "Singapore", CurrencyCode = "SGD", MinorUnits = 2 };
    private static readonly Market Kr = new() { Code = "KR", Name = "South Korea", CurrencyCode = "KRW", MinorUnits = 0 };

    private const string Claimant = "claimant-sg";
    private const string Handler = "handler-1";
    private static readonly User HandlerUser = new() { Id = Handler, DisplayName = "Priya", Role = UserRole.Handler };
    private const string OtherHandler = "handler-2";

    private static Claim.NewClaim Motor(decimal amount = 4200.00m, DateOnly? incident = null) => new(
        ClaimType.Motor, "POL-SG-123456", incident ?? new DateOnly(2026, 11, 15), "Orchard Road",
        "Rear-ended at a traffic light, bumper and boot damaged.", amount, "SGX1234A", null);

    private static Claim Submitted(Market? market = null, decimal amount = 4200.00m) =>
        Claim.Submit("CLM-SG-000001", market ?? Sg, Claimant, Motor(amount), Now);

    private static Claim UnderReview()
    {
        var c = Submitted();
        c.PickUp(HandlerUser, Now);
        return c;
    }

    // ------------------------------------------------------------ submission

    [Fact]
    public void Submit_derives_currency_from_market_and_sets_initial_reserve()
    {
        var c = Submitted();

        Assert.Equal(ClaimStatus.Submitted, c.Status);
        Assert.Equal("SGD", c.CurrencyCode);          // DR-2
        Assert.Equal(4200.00m, c.ReserveAmount);      // RS-1
        Assert.Single(c.Events);
        Assert.Equal(ClaimEventType.Submitted, c.Events[0].Type);
        Assert.Equal(1, c.Events[0].Sequence);
    }

    [Fact]
    public void Submit_rejects_future_incident_date()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Claim.Submit("CLM-SG-000001", Sg, Claimant, Motor(incident: new DateOnly(2026, 12, 1)), Now));
        Assert.Equal("IncidentDate", ex.Field);
    }

    [Fact]
    public void Motor_claim_requires_vehicle_registration()
    {
        var input = Motor() with { VehicleRegistration = null };
        Assert.Throws<DomainValidationException>(() => Claim.Submit("CLM-SG-000001", Sg, Claimant, input, Now));
    }

    [Fact]
    public void Property_claim_requires_address_and_drops_vehicle_fields()
    {
        var input = Motor() with { Type = ClaimType.Property, PropertyAddress = null };
        Assert.Throws<DomainValidationException>(() => Claim.Submit("CLM-SG-000001", Sg, Claimant, input, Now));

        var ok = Claim.Submit("CLM-SG-000001", Sg, Claimant, input with { PropertyAddress = "1 Marina Blvd" }, Now);
        Assert.Null(ok.VehicleRegistration);
    }

    // ------------------------------------------------------------ money precision (DR-3)

    [Theory]
    [InlineData(1000.5, 0, false)]   // KRW: no decimals
    [InlineData(1000, 0, true)]
    [InlineData(10.123, 2, false)]   // SGD/MYR: max 2
    [InlineData(10.12, 2, true)]
    [InlineData(10.10, 2, true)]
    public void Precision_follows_currency_minor_units(double amount, int minorUnits, bool valid)
    {
        Assert.Equal(valid, Money.HasValidPrecision((decimal)amount, minorUnits));
    }

    [Fact]
    public void Krw_claim_with_decimals_is_rejected()
    {
        Assert.Throws<DomainValidationException>(() => Submitted(Kr, 3_500_000.5m));
        Assert.Equal(3_500_000m, Submitted(Kr, 3_500_000m).ClaimedAmount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Claimed_amount_must_be_positive(int amount)
    {
        Assert.Throws<DomainValidationException>(() => Submitted(amount: amount));
    }

    // ------------------------------------------------------------ handler actions

    [Fact]
    public void Pick_up_assigns_handler()
    {
        var c = UnderReview();
        Assert.Equal(ClaimStatus.UnderReview, c.Status);
        Assert.Equal(Handler, c.AssignedHandlerId);
    }

    [Theory]
    [InlineData(UserRole.Claimant)]
    [InlineData(UserRole.Manager)]
    public void Only_handlers_can_pick_up(UserRole role)
    {
        var c = Submitted();
        Assert.Throws<ForbiddenException>(() => c.PickUp(new User { Id = "x", DisplayName = "X", Role = role }, Now));
        Assert.Equal(ClaimStatus.Submitted, c.Status);
    }

    [Fact]
    public void Only_the_assigned_handler_can_act()
    {
        var c = UnderReview();
        Assert.Throws<ForbiddenException>(() => c.RequestInformation(OtherHandler, "Photos please", Now));
        Assert.Throws<ForbiddenException>(() => c.Approve(OtherHandler, 100m, null, Now));
        Assert.Throws<ForbiddenException>(() => c.Reject(OtherHandler, "No cover", Now));
        Assert.Throws<ForbiddenException>(() => c.AdjustReserve(OtherHandler, 100m, "x", Now));
    }

    [Fact]
    public void Information_loop_round_trips()
    {
        var c = UnderReview();

        c.RequestInformation(Handler, "Please upload photos of the damage.", Now);
        Assert.Equal(ClaimStatus.AwaitingInfo, c.Status);
        Assert.NotNull(c.OpenInformationRequest);

        c.ProvideInformation(Claimant, "Photos emailed to the branch.", Now);
        Assert.Equal(ClaimStatus.UnderReview, c.Status);
        Assert.Null(c.OpenInformationRequest);
        Assert.Equal("Photos emailed to the branch.", c.InformationRequests.Single().Response);
    }

    [Fact]
    public void Responding_when_nothing_was_asked_is_a_conflict()
    {
        var c = UnderReview();
        Assert.Throws<InvalidTransitionException>(() => c.ProvideInformation(Claimant, "hello", Now));
    }

    [Fact]
    public void Another_claimant_cannot_respond()
    {
        var c = UnderReview();
        c.RequestInformation(Handler, "Photos?", Now);
        Assert.Throws<NotFoundException>(() => c.ProvideInformation("claimant-kr", "hi", Now));
    }

    [Fact]
    public void Reserve_adjustment_records_old_and_new_values_as_internal_event()
    {
        var c = UnderReview();
        c.AdjustReserve(Handler, 3500.00m, "Repair quote received", Now);

        var e = c.Events.Last();
        Assert.Equal(ClaimEventType.ReserveAdjusted, e.Type);
        Assert.Equal(4200.00m, e.PreviousAmount);
        Assert.Equal(3500.00m, e.Amount);
        Assert.True(e.IsInternal);
        Assert.Equal(3500.00m, c.ReserveAmount);
    }

    [Fact]
    public void Reserve_cannot_be_adjusted_after_a_decision()
    {
        var c = UnderReview();
        c.Approve(Handler, 4000m, null, Now);
        Assert.Throws<ConflictException>(() => c.AdjustReserve(Handler, 1m, "late", Now));
    }

    [Fact]
    public void Approve_cannot_exceed_claimed_amount()
    {
        var c = UnderReview();
        Assert.Throws<DomainValidationException>(() => c.Approve(Handler, 4200.01m, null, Now));
    }

    [Fact]
    public void Approve_then_settle_moves_reserve_correctly()
    {
        var c = UnderReview();

        c.Approve(Handler, 3900.00m, "Approved per repair quote", Now);
        Assert.Equal(ClaimStatus.Approved, c.Status);
        Assert.Equal(3900.00m, c.ReserveAmount);   // RS-3: reserve = approved amount
        Assert.True(c.IsOpen);                     // still counts towards exposure until paid

        c.Settle(Handler, Now);
        Assert.Equal(ClaimStatus.Settled, c.Status);
        Assert.Equal(3900.00m, c.SettledAmount);
        Assert.Equal(0m, c.ReserveAmount);         // RS-3: released
        Assert.False(c.IsOpen);
    }

    [Fact]
    public void Reject_requires_reason_and_releases_reserve()
    {
        var c = UnderReview();
        Assert.Throws<DomainValidationException>(() => c.Reject(Handler, " ", Now));

        c.Reject(Handler, "Policy lapsed before incident date.", Now);
        Assert.Equal(ClaimStatus.Rejected, c.Status);
        Assert.Equal(0m, c.ReserveAmount);
        Assert.NotNull(c.ClosedAt);
    }

    [Fact]
    public void Reject_while_awaiting_info_closes_the_open_request()
    {
        var c = UnderReview();
        c.RequestInformation(Handler, "Police report?", Now);
        c.Reject(Handler, "No response after 30 days.", Now);

        Assert.Equal(ClaimStatus.Rejected, c.Status);
        Assert.Null(c.OpenInformationRequest);
    }

    [Fact]
    public void Event_sequence_increments_once_per_action()
    {
        var c = UnderReview();
        c.RequestInformation(Handler, "Photos?", Now);
        c.ProvideInformation(Claimant, "Sent", Now);
        c.Approve(Handler, 100m, null, Now);
        c.Settle(Handler, Now);

        Assert.Equal([1, 2, 3, 4, 5, 6], c.Events.Select(e => e.Sequence));
        Assert.Equal(6, c.LastEventSequence);
    }
}
