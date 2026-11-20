using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Insights.Api.Auth;

/// <summary>
/// DEVELOPMENT-ONLY identity for Insights (N-3), the counterpart of Claims' DevHeader scheme.
///
/// Insights deliberately has no user store and never calls the Claims service: if it did, a Claims
/// outage would lock managers out of the dashboard, which defeats the point of the split (E-4).
/// So it does what it would do with a real token: it trusts the identity claims the caller presents.
/// In production that's a signed JWT (sub + role), validated with AddJwtBearer against the same
/// identity provider as Claims. In dev it's two unsigned headers, X-User-Id and X-User-Role, which is
/// exactly as spoofable as X-User-Id is on the Claims side. Endpoints and policies don't change
/// when the real scheme is swapped in.
/// </summary>
public class DevIdentityHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevHeader";
    public const string UserHeader = "X-User-Id";
    public const string RoleHeader = "X-User-Role";
    public static readonly string[] Roles = ["Claimant", "Handler", "Manager"];

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var id = Request.Headers[UserHeader].ToString();
        if (string.IsNullOrWhiteSpace(id))
            return Task.FromResult(AuthenticateResult.NoResult());

        var role = Request.Headers[RoleHeader].ToString();
        if (!Roles.Contains(role))
            return Task.FromResult(AuthenticateResult.Fail($"{RoleHeader} must be one of {string.Join(", ", Roles)}."));

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Role, role)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

public static class Policies
{
    public const string Claimant = nameof(Claimant);
    /// <summary>Handlers and managers. The brief has staff seeing team workload and performance.</summary>
    public const string Staff = nameof(Staff);
}

/// <summary>Adds both dev headers to the OpenAPI document so Swagger UI's "Authorize" box works.</summary>
public class DevIdentityOpenApiTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Info.Title = "Chubb APAC Claims Insights API";
        document.Info.Description =
            "Read-only dashboards built from Kafka claim events. Eventually consistent with the Claims API. " +
            "Dev identity: X-User-Id plus X-User-Role (Claimant, Handler or Manager), standing in for a JWT's sub " +
            "and role claims. Not production authentication.";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Security ??= [];
        foreach (var (name, header, description) in new[]
                 {
                     ("DevUser", DevIdentityHandler.UserHeader, "User id, e.g. manager-1 or claimant-sg"),
                     ("DevRole", DevIdentityHandler.RoleHeader, "Claimant, Handler or Manager"),
                 })
        {
            document.Components.SecuritySchemes[name] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = header, Description = description,
            };
        }
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("DevUser", document)] = [],
            [new OpenApiSecuritySchemeReference("DevRole", document)] = [],
        });
        return Task.CompletedTask;
    }
}
