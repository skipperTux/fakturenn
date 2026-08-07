namespace Fakturenn.SharedKernel;

/// <summary>An amount bound to an ISO 4217 currency code.</summary>
public readonly record struct Money
{
    // public Constructors
    public Money(decimal amount, string currency)
    {
        if (!IsIso4217Code(currency))
        {
            throw new ArgumentException(
                $"'{currency}' is not a three-letter uppercase ISO 4217 currency code.",
                nameof(currency));
        }

        Amount = amount;
        Currency = currency;
    }

    // public Properties
    public decimal Amount { get; }

    public string Currency { get; }

    // public static Methods
    public static Money operator +(Money left, Money right)
    {
        if (left.Currency != right.Currency)
        {
            throw new InvalidOperationException(
                $"Cannot add {left.Currency} to {right.Currency}.");
        }

        return new Money(left.Amount + right.Amount, left.Currency);
    }

    // public Methods
    /// <summary>
    /// Rounds to two decimal places away from zero. Commercial rounding is used
    /// rather than banker's rounding because invoice totals must match what a
    /// human arrives at with the same figures.
    /// </summary>
    public Money Round() =>
        new(Math.Round(Amount, 2, MidpointRounding.AwayFromZero), Currency);

    public override string ToString() => $"{Amount} {Currency}";

    // private static Methods
    private static bool IsIso4217Code(string currency)
    {
        if (currency is not { Length: 3 })
        {
            return false;
        }

        foreach (char character in currency)
        {
            if (character is < 'A' or > 'Z')
            {
                return false;
            }
        }

        return true;
    }
}
