using System.Text.Json.Serialization;
using Claims.Api.Auth;
using Claims.Api.Data;
using Claims.Api.Domain;
using Claims.Api.Features;
using Claims.Api.Outbox;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ClaimsDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Claims")));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ClaimRepository>();
builder.Services.AddScoped<ClaimCommands>();
builder.Services.AddScoped<ClaimQueries>();
builder.Services.AddScoped<Seeder>();

builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));
if (builder.Configuration.GetValue("Outbox:Enabled", true))
    builder.Services.AddHostedService<OutboxPublisher>();

builder.Services
    .AddAuthentication(DevHeaderAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DevHeaderAuthenticationHandler>(DevHeaderAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Claimant, p => p.RequireRole(nameof(UserRole.Claimant)))
    .AddPolicy(Policies.Handler, p => p.RequireRole(nameof(UserRole.Handler)))
    .AddPolicy(Policies.Staff, p => p.RequireRole(nameof(UserRole.Handler), nameof(UserRole.Manager)));

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddOpenApi(o => o.AddDocumentTransformer<DevHeaderOpenApiTransformer>());

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
    .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/openapi/v1.json", "Claims API");
    o.DocumentTitle = "Claims API";
});
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();
app.MapClaimsApi();

// Apply migrations and seed on start-up. Fine for a local demo; production would run
// migrations as a separate deployment step.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<Seeder>()
        .SeedAsync(includeDemoClaims: app.Configuration.GetValue("Seed:DemoClaims", true));
}

app.Run();

public partial class Program; // for WebApplicationFactory in tests
