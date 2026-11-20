namespace Claims.Api.Domain;

/// <summary>Money rules (DR-3). Amounts are always decimal and always in the claim's own currency.</summary>
public static class Money
{
    /// <summary>True when the amount has no more decimal places than the currency allows.</summary>
    public static bool HasValidPrecision(decimal amount, int minorUnits) =>
        decimal.Round(amount, minorUnits) == amount;

    public static void EnsureValid(string field, decimal amount, int minorUnits, bool allowZero = false)
    {
        if (amount < 0 || (!allowZero && amount == 0))
            throw new DomainValidationException(field, allowZero ? "Amount cannot be negative." : "Amount must be greater than zero.");

        if (!HasValidPrecision(amount, minorUnits))
            throw new DomainValidationException(field,
                minorUnits == 0
                    ? "This currency does not allow decimal places."
                    : $"This currency allows at most {minorUnits} decimal places.");
    }
}
