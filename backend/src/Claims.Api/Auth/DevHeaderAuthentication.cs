using System.Security.Claims;
using System.Text.Encodings.Web;
using Claims.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Claims.Api.Auth;

/// <summary>
/// DEVELOPMENT-ONLY identity (N-3). The caller says who they are with an X-User-Id header, and we
/// look the user up in the seeded Users table.
///
/// This is NOT authentication. It is a stand-in that plugs into the real ASP.NET auth pipeline, so
/// moving to production is a configuration change (AddJwtBearer against the identity provider).
/// Endpoints, policies and the CurrentUser accessor stay the same.
/// </summary>
public class DevHeaderAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ClaimsDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevHeader";
    public const string Header = "X-User-Id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var values) || string.IsNullOrWhiteSpace(values))
            return AuthenticateResult.NoResult();

        var id = values.ToString();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, Context.RequestAborted);
        if (user is null)
            return AuthenticateResult.Fail($"Unknown user '{id}'.");

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
        ], SchemeName);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

public static class Policies
{
    public const string Claimant = nameof(Claimant);
    public const string Handler = nameof(Handler);
    public const string Staff = nameof(Staff); // handlers and managers
}

public static class PrincipalExtensions
{
    public static string UserId(this ClaimsPrincipal p) =>
        p.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("No authenticated user.");
}
