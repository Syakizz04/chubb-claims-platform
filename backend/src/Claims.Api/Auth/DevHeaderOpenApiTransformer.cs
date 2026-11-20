using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Claims.Api.Auth;

/// <summary>Adds the X-User-Id header to the OpenAPI document so Swagger UI has an "Authorize" box.</summary>
public class DevHeaderOpenApiTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Info.Title = "Chubb APAC Claims API";
        document.Info.Description =
            "Claim lifecycle API. Dev identity: set X-User-Id to a seeded user, e.g. claimant-sg, handler-1, manager-1 " +
            "(see GET /api/dev/users). Not production authentication.";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[DevHeaderAuthenticationHandler.SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = DevHeaderAuthenticationHandler.Header,
            Description = "Seeded user id",
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(DevHeaderAuthenticationHandler.SchemeName, document)] = [],
        });
        return Task.CompletedTask;
    }
}
