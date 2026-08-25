using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Postgresql;

namespace Fakturenn.Infrastructure.Messaging;

/// <summary>
/// Registers Wolverine with PostgreSQL-backed durable local queues.
/// <para>
/// Infrastructure rather than a module: no module owns "messaging", and
/// MODULE-OWNERSHIP.md's OutboundMessage belongs to Mail, which is application-level
/// delivery tracking rather than this transport storage. Modules never reference this
/// assembly; architecture rule 4 enforces that by name pattern.
/// </para>
/// </summary>
public static class MessagingConfiguration
{
    /// <summary>
    /// Wolverine's own envelope, dead-letter and node tables live here. Fourth schema
    /// alongside identity, invoices and dataprotection.
    /// </summary>
    public const string SchemaName = "messaging";

    // public Methods
    public static void AddFakturennMessaging(this IHostApplicationBuilder builder, string? connectionString)
    {
        builder.UseWolverine(options =>
        {
            options.PersistMessagesWithPostgresql(connectionString ?? string.Empty, SchemaName);

            // Durable, not in-memory. This is the choice that makes the storage real --
            // an in-memory transport would pass every delivery test and lose everything
            // on restart, which is why a test asserts an envelope reaches the table.
            options.Policies.UseDurableLocalQueues();

            // Deliberately NOT calling UseResourceSetupOnStartup(). Wolverine creates its
            // schema on boot when that is present, and CLAUDE.md's invariant is that
            // migrations never run automatically at startup -- only --migrate touches the
            // schema. MessagingStorage.ProvisionAsync is how the tables get created.
        });
    }
}
