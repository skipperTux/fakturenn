namespace Fakturenn.SharedKernel;

/// <summary>A percentage expressed in whole percent, so 19 means 19%.</summary>
public readonly record struct Percentage
{
    // public Constructors
    public Percentage(decimal value)
    {
        if (value is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value, "A percentage must be between 0 and 100.");
        }

        Value = value;
    }

    // public Properties
    public decimal Value { get; }

    // public Methods
    public Money Of(Money money) =>
        new Money(money.Amount * Value / 100m, money.Currency).Round();

    public override string ToString() => $"{Value}%";
}
