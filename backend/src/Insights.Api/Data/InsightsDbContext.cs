using Insights.Api.Projection;
using Microsoft.EntityFrameworkCore;

namespace Insights.Api.Data;

public class InsightsDbContext(DbContextOptions<InsightsDbContext> options) : DbContext(options)
{
    public DbSet<ClaimSnapshot> Claims => Set<ClaimSnapshot>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<FxRate> FxRates => Set<FxRate>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b) =>
        b.Properties<decimal>().HavePrecision(19, 4);

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ClaimSnapshot>(e =>
        {
            e.ToTable("ClaimSnapshots");
            e.HasKey(c => c.ClaimId);
            e.Property(c => c.ClaimId).ValueGeneratedNever();
            e.HasIndex(c => new { c.Status, c.MarketCode });
            e.HasIndex(c => c.HandlerId);
            e.Ignore(c => c.IsOpen);
        });

        b.Entity<ProcessedEvent>(e =>
        {
            e.HasKey(p => p.EventId);
            e.Property(p => p.EventId).ValueGeneratedNever();
        });

        b.Entity<Notification>(e =>
        {
            e.HasKey(n => n.Id);
            e.HasIndex(n => n.EventId).IsUnique(); // one notification per event, ever
            e.HasIndex(n => new { n.ClaimantId, n.CreatedAt });
        });

        b.Entity<FxRate>(e =>
        {
            e.HasKey(f => f.CurrencyCode);
            e.Property(f => f.UsdPerUnit).HasPrecision(19, 10);
            e.HasData(FxRate.Seed);
        });
    }
}

/// <summary>E-3: the event ids we've already handled. Checked and written in the same transaction as the projection.</summary>
public class ProcessedEvent
{
    public Guid EventId { get; init; }
    public DateTimeOffset ProcessedAt { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}

/// <summary>E-5: claimant notifications. Stored and logged only; delivery (email/SMS) is out of scope.</summary>
public class Notification
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid EventId { get; init; }
    public Guid ClaimId { get; init; }
    public required string ClaimantId { get; init; }
    public required string Reference { get; init; }
    public required string Message { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Static, seeded FX rates for the indicative USD total (M-2). Production would use a daily rates
/// feed, with period-end rates for financial reporting.
/// </summary>
public class FxRate
{
    public required string CurrencyCode { get; init; }
    public required decimal UsdPerUnit { get; init; }
    public required DateOnly AsOf { get; init; }

    private static readonly DateOnly SeedDate = new(2026, 11, 1);

    public static readonly FxRate[] Seed =
    [
        new() { CurrencyCode = "USD", UsdPerUnit = 1m, AsOf = SeedDate },
        new() { CurrencyCode = "KRW", UsdPerUnit = 0.00072m, AsOf = SeedDate },
        new() { CurrencyCode = "HKD", UsdPerUnit = 0.1285m, AsOf = SeedDate },
        new() { CurrencyCode = "TWD", UsdPerUnit = 0.0310m, AsOf = SeedDate },
        new() { CurrencyCode = "THB", UsdPerUnit = 0.0290m, AsOf = SeedDate },
        new() { CurrencyCode = "MYR", UsdPerUnit = 0.2250m, AsOf = SeedDate },
        new() { CurrencyCode = "SGD", UsdPerUnit = 0.7700m, AsOf = SeedDate },
    ];
}
