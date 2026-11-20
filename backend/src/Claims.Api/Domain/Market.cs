namespace Claims.Api.Domain;

/// <summary>
/// A market is seeded data, not an enum (DR-1): adding a market is a data change.
/// MinorUnits follows ISO 4217 (KRW = 0; HKD, TWD, THB, MYR, SGD = 2).
/// </summary>
public class Market
{
    public required string Code { get; init; }          // ISO 3166 alpha-2, e.g. "SG"
    public required string Name { get; init; }
    public required string CurrencyCode { get; init; }  // ISO 4217, e.g. "SGD"
    public required int MinorUnits { get; init; }
}

/// <summary>Per-market counter used to build claim references like CLM-SG-000123 (DR-4).</summary>
public class MarketSequence
{
    public required string MarketCode { get; init; }
    public long LastValue { get; set; }
}

public class User
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required UserRole Role { get; init; }
}
