using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Claims.Tests.Api;

/// <summary>
/// Boots the real Claims API against a throwaway Postgres container. Real Postgres rather than an
/// in-memory provider, because concurrency (xmin) and UPDATE … RETURNING are what we're testing.
/// The Kafka publisher is turned off: these tests assert what lands in the outbox, not on Kafka.
/// </summary>
public class ClaimsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task InitializeAsync() => await _pg.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _pg.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Claims", _pg.GetConnectionString());
        builder.UseSetting("Outbox:Enabled", "false");
        builder.UseSetting("Seed:DemoClaims", "false");
    }

    public HttpClient As(string userId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", userId);
        return client;
    }

    public string ConnectionString => _pg.GetConnectionString();
}

public static class HttpExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<T>(ClaimsApiFactory.Json))!;

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient c, string url, object? body = null) =>
        c.PostAsJsonAsync(url, body ?? new { }, ClaimsApiFactory.Json);
}
