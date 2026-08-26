using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.Extensions.DependencyInjection;
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
        // Outside the UseWolverine lambda deliberately. The same condition is checked again
        // inside it, but nothing there can report anything: the lambda runs without a
        // logger, and so does registration itself. A hosted service is what gets the
        // consequence onto the host's real logging pipeline at startup.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            builder.Services.AddHostedService<NonDurableMessagingWarning>();
        }

        builder.UseWolverine(options =>
        {
            // Default TypeLoadMode is Dynamic, which compiles handler/middleware code at
            // startup through Roslyn. Core WolverineFx 6.30.0 no longer ships that
            // compiler, so Dynamic throws at host startup -- confirmed by starting the real
            // host, not read off documentation.
            //
            // Auto is "load pre-generated types from the application assembly, and generate
            // them if there are none" (JasperFx.CodeGeneration.TypeLoadMode's own summary).
            // It is NOT a reflection fallback. This project believed it was, and the belief
            // survived only because there was no handler to dispatch: the failure appears at
            // dispatch, not at startup, and the first real handler produced "No
            // IAssemblyGenerator is registered in the application's service provider, but
            // runtime code generation was requested" from AutoTypeLoader.Initialize, with a
            // healthy host and an undelivered message.
            //
            // So the generator has to exist. UseRuntimeCompilation registers it, which is
            // option (a) in Wolverine's own remediation text. Option (b), pre-generating
            // with `codegen write` and TypeLoadMode.Static, was rejected: pre-generated
            // types are loaded from the *application* assembly, so any handler living
            // elsewhere -- the integration suite's outbox probe, for one -- could never be
            // dispatched, and a forgotten regeneration is a runtime failure rather than a
            // build error.
            //
            // Both set before the connection-string guard below: WolverineRuntime.StartAsync
            // calls logCodeGenerationConfiguration() unconditionally, before it ever looks
            // at persistence, so a host with no connection string configured hits this
            // check too.
            options.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
            options.UseRuntimeCompilation();

            // No connection string mirrors the health-check branch in
            // FakturennWebApplication.Build: "not configured yet" is a first-class state,
            // not an error. Durable Postgres persistence needs a real database to validate
            // against at startup (see the AutoBuildMessageStorageOnStartup remark below), so
            // wiring it with an empty connection string would turn "no database configured"
            // into a host that cannot start at all -- breaking the documented invariant that
            // the application starts without a database. Falling through to Wolverine's
            // in-memory local queues keeps that invariant for a host with nothing configured;
            // durability is meaningless there anyway, since nothing persists it either way.
            // NonDurableMessagingWarning, registered above, is what keeps that fallback from
            // being silent.
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return;
            }

            options.PersistMessagesWithPostgresql(connectionString, SchemaName);

            // Durable, not in-memory. This is the choice that makes the storage real --
            // an in-memory transport would pass every delivery test and lose everything
            // on restart, which is why a test asserts an envelope reaches the table.
            options.Policies.UseDurableLocalQueues();

            // Deliberately NOT calling UseResourceSetupOnStartup(). Wolverine creates its
            // schema on boot when that is present, and CLAUDE.md's invariant is that
            // migrations never run automatically at startup -- only --migrate touches the
            // schema. MessagingStorage.ProvisionAsync is how the tables get created.
            //
            // That is not sufficient on its own, though: AutoBuildMessageStorageOnStartup
            // is a SEPARATE knob from UseResourceSetupOnStartup and defaults to
            // AutoCreate.CreateOrUpdate. Verified by starting the real host against an
            // unmigrated database with only the line above in place -- the messaging
            // schema and every wolverine_* table appeared anyway, logged as "Applied
            // database migration for Wolverine Envelope Storage". Turning this off is
            // what actually keeps startup from touching the schema.
            //
            // It does not make startup tolerant of a missing schema, though -- verified by
            // reading WolverineRuntime.HostService.cs (tryMigrateStorage,
            // loadAgentRestrictionsAsync) and NodeAgentController.StartLocally.cs
            // (StartSoloModeAsync): every durability mode that keeps local queues live
            // (Balanced, Solo) queries messaging.wolverine_nodes unconditionally during
            // WolverineRuntime.StartAsync, with no ResourceMigrationFailureMode guard around
            // that particular query. A host with a real, unmigrated connection string fails
            // to start -- which is the correct failure shape here: DEPLOYMENT-BASELINE.md's
            // migration Job always runs before a replica serves traffic, so a host that
            // reaches this point with a real connection string and no schema has skipped a
            // required step, and the intended thing to fail is startup itself, not launch
            // and quietly serve traffic with delivery guarantees that turned out imaginary.
            options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
        });
    }
}
