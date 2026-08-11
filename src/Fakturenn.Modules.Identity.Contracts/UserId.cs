using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Identity.Contracts;

/// <summary>
/// The Identity module's user identifier as other modules see it. Cross-module
/// references use this, never <c>ApplicationUser</c>.
/// </summary>
public readonly record struct UserId
{
    // public Constructors
    public UserId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A user id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    // public Properties
    public Guid Value { get; }

    // public static Methods
    public static UserId New(IIdGenerator generator) => new(generator.NewId());

    // public Methods
    public override string ToString() => Value.ToString();
}
