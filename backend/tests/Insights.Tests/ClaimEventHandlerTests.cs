using Insights.Api.Data;
using Insights.Api.Projection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace Insights.Tests;

/// <summary>E-3 against a real database: the dedupe record and the projection commit together.</summary>
public class ClaimEventHandlerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        await using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    private InsightsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<InsightsDbContext>().UseNpgsql(_pg.GetConnectionString()).Options);

    private async Task<ClaimEventHandler.Outcome> Handle(Claims.Contracts.ClaimEventMessage e)
    {
        await using var db = NewDb();
        return await new ClaimEventHandler(db, TimeProvider.System, NullLogger<ClaimEventHandler>.Instance)
            .HandleAsync(e, CancellationToken.None);
    }

    [Fact]
    public async Task Redelivered_event_is_a_no_op()
    {
        var stream = TestEvents.FullLifecycle();
        foreach (var e in stream) Assert.Equal(ClaimEventHandler.Outcome.Applied, await Handle(e));

        // Kafka redelivers the whole stream (e.g. a consumer crashed before committing offsets).
        foreach (var e in stream) Assert.Equal(ClaimEventHandler.Outcome.Duplicate, await Handle(e));

        await using var db = NewDb();
        var snapshot = await db.Claims.SingleAsync(c => c.ClaimId == stream[0].ClaimId);
        Assert.Equal("Settled", snapshot.Status);
        Assert.Equal(5, await db.ProcessedEvents.CountAsync());
        // Submitted, Approved and Settled each notify exactly once, despite the redelivery.
        Assert.Equal(3, await db.Notifications.CountAsync(n => n.ClaimId == stream[0].ClaimId));
    }

    [Fact]
    public async Task Late_event_is_recorded_but_does_not_regress_state()
    {
        var stream = TestEvents.FullLifecycle();
        await Handle(stream[3]); // approved arrives first
        Assert.Equal(ClaimEventHandler.Outcome.Stale, await Handle(stream[1]));

        await using var db = NewDb();
        Assert.Equal("Approved", (await db.Claims.SingleAsync(c => c.ClaimId == stream[0].ClaimId)).Status);
    }

    [Fact]
    public async Task Fx_rates_are_seeded_by_migration()
    {
        await using var db = NewDb();
        var codes = await db.FxRates.Select(r => r.CurrencyCode).ToListAsync();
        Assert.Equivalent(new[] { "USD", "KRW", "HKD", "TWD", "THB", "MYR", "SGD" }, codes);
    }
}
