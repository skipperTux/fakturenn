using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Invoices.Contracts;

/// <summary>
/// The Invoices module's identifier as other modules see it. Cross-module
/// references use this, never the module's EF entity.
/// </summary>
public readonly record struct InvoiceId
{
    // public Constructors
    public InvoiceId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An invoice id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    // public Properties
    public Guid Value { get; }

    // public static Methods
    public static InvoiceId New(IIdGenerator generator) => new(generator.NewId());

    // public Methods
    public override string ToString() => Value.ToString();
}
