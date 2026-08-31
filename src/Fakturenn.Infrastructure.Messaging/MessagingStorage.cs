using Microsoft.Extensions.DependencyInjection;
using Wolverine.Persistence.Durability;

namespace Fakturenn.Infrastructure.Messaging;

/// <summary>
/// Creates Wolverine's tables, on demand and never at startup.
/// <para>
/// Wolverine owns these table definitions and changes them between versions. Writing EF
/// migrations that mirror a library's private schema would mean re-deriving them on every
/// upgrade, and getting that wrong fails at runtime inside message storage rather than at
/// build time. So the library owns the DDL and this project owns the timing.
/// </para>
/// </summary>
public static class MessagingStorage
{
    // public Methods
    public static async Task ProvisionAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        // IMessageStoreAdmin.MigrateAsync has no CancellationToken overload in the restored
        // WolverineFx.Postgresql version (only a parameterless overload and one taking an
        // AutoCreate? override). Kept as a parameter here regardless: callers pass one, and the
        // signature should not churn the day the library adds it.
        cancellationToken.ThrowIfCancellationRequested();

        IMessageStore store = services.GetRequiredService<IMessageStore>();
        await store.Admin.MigrateAsync();
    }
}
