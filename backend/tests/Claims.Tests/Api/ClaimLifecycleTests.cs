using System.Net;
using Claims.Api.Domain;
using Claims.Api.Features;
using Npgsql;

namespace Claims.Tests.Api;

public class ClaimLifecycleTests(ClaimsApiFactory factory) : IClassFixture<ClaimsApiFactory>
{
    private static object MotorClaim(string market = "SG", decimal amount = 4200.00m) => new
    {
        marketCode = market,
        type = "Motor",
        policyNumber = $"POL-{market}-123456",
        incidentDate = "2026-11-01",
        incidentLocation = "Orchard Road",
        description = "Rear-ended at a traffic light, bumper damaged.",
        claimedAmount = amount,
        vehicleRegistration = "SGX1234A",
    };

    private async Task<ClaimantClaimDetail> SubmitAsync(string claimant = "claimant-sg", string market = "SG", decimal amount = 4200.00m)
    {
        var r = await factory.As(claimant).PostJsonAsync("/api/claimant/claims", MotorClaim(market, amount));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return await r.ReadAsync<ClaimantClaimDetail>();
    }

    [Fact]
    public async Task Happy_path_submit_to_settle_with_information_loop()
    {
        var claimant = factory.As("claimant-sg");
        var handler = factory.As("handler-1");

        // C-1: submit
        var submitted = await SubmitAsync();
        Assert.Matches(@"^CLM-SG-\d{6}$", submitted.Reference);
        Assert.Equal(ClaimStatus.Submitted, submitted.Status);
        var id = submitted.Id;

        // H-1: it's in the queue
        var queue = await (await handler.GetAsync("/api/staff/queue?market=SG")).ReadAsync<List<StaffClaimSummary>>();
        Assert.Contains(queue, c => c.Id == id);

        // H-2: pick up
        var picked = await (await handler.PostJsonAsync($"/api/staff/claims/{id}/pick-up")).ReadAsync<StaffClaimDetail>();
        Assert.Equal(ClaimStatus.UnderReview, picked.Status);
        Assert.Contains("Approve", picked.AvailableActions);

        // H-5 → C-5: information loop
        await handler.PostJsonAsync($"/api/staff/claims/{id}/information-requests", new { question = "Please send photos of the damage." });
        var awaiting = await (await claimant.GetAsync($"/api/claimant/claims/{id}")).ReadAsync<ClaimantClaimDetail>();
        Assert.Equal(ClaimStatus.AwaitingInfo, awaiting.Status);
        Assert.Equal("Please send photos of the damage.", awaiting.OpenInformationRequest!.Question);

        var answered = await claimant.PostJsonAsync($"/api/claimant/claims/{id}/information-response", new { response = "Photos uploaded to the portal." });
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);

        // RS-2: adjust reserve, H-7: approve, H-9: settle
        Assert.Equal(HttpStatusCode.OK, (await handler.PostJsonAsync($"/api/staff/claims/{id}/reserve", new { amount = 3900.00m, reason = "Repair quote" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await handler.PostJsonAsync($"/api/staff/claims/{id}/approve", new { amount = 3900.00m })).StatusCode);
        var settled = await (await handler.PostJsonAsync($"/api/staff/claims/{id}/settle")).ReadAsync<StaffClaimDetail>();

        Assert.Equal(ClaimStatus.Settled, settled.Status);
        Assert.Equal(3900.00m, settled.SettledAmount);
        Assert.Equal(0m, settled.ReserveAmount);

        // C-6: the claimant sees the decision, and the timeline hides internal events (C-4)
        var final = await (await claimant.GetAsync($"/api/claimant/claims/{id}")).ReadAsync<ClaimantClaimDetail>();
        Assert.Equal(3900.00m, final.SettledAmount);
        Assert.DoesNotContain(final.Timeline, e => e.Type == ClaimEventType.ReserveAdjusted);
        Assert.Equal(ClaimStatus.Settled, final.Status);

        // E-2: one outbox row per event, in the same transactions
        Assert.Equal(settled.Timeline.Count, await OutboxCountAsync(id));
    }

    [Fact]
    public async Task Concurrent_pick_up_has_exactly_one_winner()
    {
        var claim = await SubmitAsync();

        // H-2: fire several pick-ups at once from different handlers.
        var handlers = new[] { "handler-1", "handler-2", "handler-3", "handler-1", "handler-2", "handler-3" };
        var responses = await Task.WhenAll(handlers.Select(h =>
            factory.As(h).PostJsonAsync($"/api/staff/claims/{claim.Id}/pick-up")));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));

        // Only one PickedUp event was ever recorded.
        var detail = await (await factory.As("manager-1").GetAsync($"/api/staff/claims/{claim.Id}")).ReadAsync<StaffClaimDetail>();
        Assert.Single(detail.Timeline, e => e.Type == ClaimEventType.PickedUp);
    }

    [Fact]
    public async Task Claimants_cannot_see_other_claimants_claims()
    {
        var claim = await SubmitAsync("claimant-sg");

        var other = factory.As("claimant-kr");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/claimant/claims/{claim.Id}")).StatusCode);
        Assert.DoesNotContain(await (await other.GetAsync("/api/claimant/claims")).ReadAsync<List<ClaimantClaimSummary>>(),
            c => c.Id == claim.Id);
    }

    [Fact]
    public async Task Claimant_view_never_exposes_reserve()
    {
        var claim = await SubmitAsync();
        var json = await (await factory.As("claimant-sg").GetAsync($"/api/claimant/claims/{claim.Id}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("reserve", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("claimant-sg", "/api/staff/queue", HttpStatusCode.Forbidden)]
    [InlineData("handler-1", "/api/claimant/claims", HttpStatusCode.Forbidden)]
    [InlineData("manager-1", "/api/staff/my-claims", HttpStatusCode.Forbidden)]
    [InlineData(null, "/api/staff/queue", HttpStatusCode.Unauthorized)]
    public async Task Routes_are_protected_by_role(string? user, string url, HttpStatusCode expected)
    {
        var client = user is null ? factory.CreateClient() : factory.As(user);
        Assert.Equal(expected, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Illegal_transition_returns_409_with_current_status()
    {
        var claim = await SubmitAsync();
        var r = await factory.As("handler-1").PostJsonAsync($"/api/staff/claims/{claim.Id}/settle");

        // Not assigned yet, so 403 comes first; pick up, then settle before approval.
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        await factory.As("handler-1").PostJsonAsync($"/api/staff/claims/{claim.Id}/pick-up");
        r = await factory.As("handler-1").PostJsonAsync($"/api/staff/claims/{claim.Id}/settle");

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("\"currentStatus\":\"UnderReview\"", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Validation_returns_all_field_errors()
    {
        var r = await factory.As("claimant-kr").PostJsonAsync("/api/claimant/claims", new
        {
            marketCode = "KR", type = "Motor", policyNumber = "x", incidentDate = "2026-11-01",
            incidentLocation = "", description = "short", claimedAmount = 0,
        });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        foreach (var field in new[] { "policyNumber", "incidentLocation", "description", "claimedAmount" })
            Assert.Contains($"\"{field}\"", body);
    }

    [Fact]
    public async Task Krw_amounts_with_decimals_are_rejected()
    {
        var r = await factory.As("claimant-kr").PostJsonAsync("/api/claimant/claims", MotorClaim("KR", 1_000_000.5m));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task References_are_sequential_per_market()
    {
        var a = await SubmitAsync(market: "TW");
        var b = await SubmitAsync(market: "TW");
        var n = (string s) => int.Parse(s[^6..]);
        Assert.Equal(n(a.Reference) + 1, n(b.Reference));
    }

    private async Task<long> OutboxCountAsync(Guid claimId)
    {
        await using var conn = new NpgsqlConnection(factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""SELECT COUNT(*) FROM "Outbox" WHERE "Key" = @k""", conn);
        cmd.Parameters.AddWithValue("k", claimId.ToString());
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
