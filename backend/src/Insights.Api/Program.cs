using Insights.Api.Auth;
using Insights.Api.Dashboard;
using Insights.Api.Data;
using Insights.Api.Kafka;
using Insights.Api.Projection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<InsightsDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Insights")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ClaimEventHandler>();

if (builder.Configuration.GetValue("Kafka:ConsumerEnabled", true))
    builder.Services.AddHostedService<ClaimEventsConsumer>();

// Dev identity (see Auth/DevIdentity.cs). In production: AddJwtBearer against the same IdP as Claims.
builder.Services
    .AddAuthentication(DevIdentityHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DevIdentityHandler>(DevIdentityHandler.SchemeName, null);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Claimant, p => p.RequireRole("Claimant"))
    .AddPolicy(Policies.Staff, p => p.RequireRole("Handler", "Manager"));

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(o => o.AddDocumentTransformer<DevIdentityOpenApiTransformer>());
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
    o.SwaggerEndpoint("/openapi/v1.json", "Insights API");
    o.DocumentTitle = "Insights API";
});
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();
app.MapDashboardApi();

using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<InsightsDbContext>().Database.MigrateAsync();

app.Run();

public partial class Program;
