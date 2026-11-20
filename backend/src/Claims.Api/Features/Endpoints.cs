using System.Security.Claims;
using Claims.Api.Auth;
using Claims.Api.Data;
using Claims.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Features;

/// <summary>
/// Routes are grouped by audience (/claimant, /staff). Each group gets its own authorization
/// policy and its own view models, so a claimant can't reach staff data by changing a URL.
/// </summary>
public static class Endpoints
{
    public static void MapClaimsApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // ------------------------------------------------------------ reference and identity

        api.MapGet("/markets", async (ClaimsDbContext db, CancellationToken ct) =>
                await db.Markets.AsNoTracking().OrderBy(m => m.Name)
                    .Select(m => new MarketDto(m.Code, m.Name, m.CurrencyCode, m.MinorUnits, m.TimeZoneId)).ToListAsync(ct))
            .WithTags("Reference");

        api.MapGet("/dev/users", async (ClaimsDbContext db, CancellationToken ct) =>
                await db.Users.AsNoTracking().OrderBy(u => u.Role).ThenBy(u => u.DisplayName)
                    .Select(u => new UserDto(u.Id, u.DisplayName, u.Role)).ToListAsync(ct))
            .WithTags("Dev identity")
            .WithSummary("Seeded users for the dev user switcher (N-3). Not for production.");

        api.MapGet("/me", (ClaimsPrincipal user) => new
            {
                id = user.UserId(),
                displayName = user.FindFirstValue(ClaimTypes.Name),
                role = user.FindFirstValue(ClaimTypes.Role),
            })
            .RequireAuthorization()
            .WithTags("Dev identity");

        // ------------------------------------------------------------ claimant

        var claimant = api.MapGroup("/claimant/claims").RequireAuthorization(Policies.Claimant).WithTags("Claimant");

        claimant.MapPost("/", async (SubmitClaimRequest req, ClaimsPrincipal user, ClaimCommands cmd, ClaimQueries q, CancellationToken ct) =>
            {
                var claim = await cmd.SubmitAsync(user.UserId(), req, ct);
                var detail = await q.ClaimantDetailAsync(user.UserId(), claim.Id, ct);
                return Results.Created($"/api/claimant/claims/{claim.Id}", detail);
            })
            .WithSummary("C-1: report an incident");

        claimant.MapGet("/", (ClaimsPrincipal user, ClaimQueries q, CancellationToken ct) =>
                q.ForClaimantAsync(user.UserId(), ct))
            .WithSummary("C-3: my claims, newest first");

        claimant.MapGet("/{id:guid}", (Guid id, ClaimsPrincipal user, ClaimQueries q, CancellationToken ct) =>
                q.ClaimantDetailAsync(user.UserId(), id, ct))
            .WithSummary("C-4 / C-6: claim detail, timeline and decision");

        claimant.MapPost("/{id:guid}/information-response",
                async (Guid id, InformationResponseRequest req, ClaimsPrincipal user, ClaimCommands cmd, ClaimQueries q, CancellationToken ct) =>
                {
                    await cmd.ProvideInformationAsync(id, user.UserId(), req.Response, ct);
                    return await q.ClaimantDetailAsync(user.UserId(), id, ct);
                })
            .WithSummary("C-5: answer the handler's question");

        // ------------------------------------------------------------ staff (handlers and managers)

        var staff = api.MapGroup("/staff").RequireAuthorization(Policies.Staff).WithTags("Staff");

        staff.MapGet("/queue", (string? market, ClaimType? type, ClaimQueries q, CancellationToken ct) =>
                q.QueueAsync(market, type, ct))
            .WithSummary("H-1: unassigned submitted claims, oldest first");

        staff.MapGet("/my-claims", (ClaimsPrincipal user, ClaimQueries q, CancellationToken ct) =>
                q.AssignedToAsync(user.UserId(), ct))
            .RequireAuthorization(Policies.Handler)
            .WithSummary("H-3: my open claims, oldest first");

        staff.MapGet("/claims", (ClaimStatus? status, string? market, ClaimQueries q, CancellationToken ct) =>
                q.SearchAsync(status, market, ct))
            .WithSummary("All claims (max 200), newest first");

        staff.MapGet("/claims/{id:guid}", (Guid id, ClaimsPrincipal user, ClaimQueries q, CancellationToken ct) =>
                q.StaffDetailAsync(user.UserId(), user.IsInRole(nameof(UserRole.Handler)), id, ct))
            .WithSummary("H-11: full detail including reserve, internal events and available actions");

        // Handler-only commands. Each returns the refreshed claim, so the UI needs no second request.
        var handler = staff.MapGroup("/claims/{id:guid}").RequireAuthorization(Policies.Handler);

        handler.MapPost("/pick-up", (Guid id, ClaimsPrincipal u, ClaimCommands c, ClaimQueries q, CancellationToken ct) =>
                Run(id, u, q, ct, () => c.PickUpAsync(id, u.UserId(), ct)))
            .WithSummary("H-2: assign to me (exactly one of two concurrent pick-ups wins)");

        handler.MapPost("/information-requests", (Guid id, InformationRequestRequest r, ClaimsPrincipal u, ClaimCommands c, ClaimQueries q, CancellationToken ct) =>
                Run(id, u, q, ct, () => c.RequestInformationAsync(id, u.UserId(), r.Question, ct)))
            .WithSummary("H-5: ask the claimant for more information");

        handler.MapPost("/reserve", (Guid id, AdjustReserveRequest r, ClaimsPrincipal u, ClaimCommands c, ClaimQueries q, CancellationToken ct) =>
                Run(id, u, q, ct, () => c.AdjustReserveAsync(id, u.UserId(), r.Amount, r.Reason, ct)))
            .WithSummary("RS-2 / H-6: adjust the reserve");

        handler.MapPost("/approve", (Guid id, ApproveRequest r, ClaimsPrincipal u, ClaimCommands c, ClaimQueries q, CancellationToken ct) =>
                Run(id, u, q, ct, () => c.ApproveAsync(id, u.UserId(), r.Amount, r.Note, ct)))
            .WithSummary("H-7: approve");

        handler.MapPost("/reject", (Guid id, RejectRequest r, ClaimsPrincipal u, ClaimCommands c, ClaimQueries q, CancellationToken ct) =>
                Run(id, u, q, ct, () => c.RejectAsync(id, u.UserId(), r.Reason, ct)))
            .WithSummary("H-8: reject with a reason");

        handler.MapPost("/settle", (Guid id, ClaimsPrincipal u, ClaimCommands c, ClaimQueries q, CancellationToken ct) =>
                Run(id, u, q, ct, () => c.SettleAsync(id, u.UserId(), ct)))
            .WithSummary("H-9: settle an approved claim");

        staff.MapPost("/claims/{id:guid}/notes", (Guid id, AddNoteRequest r, ClaimsPrincipal u, ClaimCommands c, ClaimQueries q, CancellationToken ct) =>
                Run(id, u, q, ct, () => c.AddNoteAsync(id, u.UserId(), r.Text, ct)))
            .WithSummary("H-10: internal note (never shown to claimants)");
    }

    private static async Task<StaffClaimDetail> Run(Guid id, ClaimsPrincipal user, ClaimQueries q, CancellationToken ct, Func<Task> command)
    {
        await command();
        return await q.StaffDetailAsync(user.UserId(), user.IsInRole(nameof(UserRole.Handler)), id, ct);
    }
}
