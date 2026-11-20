using System.Security.Claims;
using System.Text.Encodings.Web;
using Insights.Api.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Insights.Tests;

/// <summary>N-3 on the Insights side: who the caller is, taken from their claims (dev headers).</summary>
public class DevIdentityTests
{
    private static async Task<AuthenticateResult> Authenticate(params (string Name, string Value)[] headers)
    {
        var context = new DefaultHttpContext();
        foreach (var (name, value) in headers) context.Request.Headers[name] = value;

        var handler = new DevIdentityHandler(new StaticOptions(), NullLoggerFactory.Instance, UrlEncoder.Default);
        await handler.InitializeAsync(new AuthenticationScheme(DevIdentityHandler.SchemeName, null, typeof(DevIdentityHandler)), context);
        return await handler.AuthenticateAsync();
    }

    [Fact]
    public async Task No_user_header_means_anonymous_so_protected_endpoints_return_401()
    {
        var result = await Authenticate();
        Assert.True(result.None);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Admin")]
    [InlineData("manager")] // roles are exact
    public async Task A_missing_or_unknown_role_is_rejected(string? role)
    {
        var headers = new List<(string, string)> { (DevIdentityHandler.UserHeader, "manager-1") };
        if (role is not null) headers.Add((DevIdentityHandler.RoleHeader, role));

        var result = await Authenticate([.. headers]);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }

    [Theory]
    [InlineData("claimant-sg", "Claimant")]
    [InlineData("handler-1", "Handler")]
    [InlineData("manager-1", "Manager")]
    public async Task A_known_role_produces_a_principal_with_id_and_role(string id, string role)
    {
        var result = await Authenticate((DevIdentityHandler.UserHeader, id), (DevIdentityHandler.RoleHeader, role));

        Assert.True(result.Succeeded);
        Assert.Equal(id, result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.True(result.Principal.IsInRole(role));
    }

    private sealed class StaticOptions : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public AuthenticationSchemeOptions CurrentValue { get; } = new();
        public AuthenticationSchemeOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }
}
