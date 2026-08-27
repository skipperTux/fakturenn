using System.Reflection;
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
    public static void AddFakturennMessaging(
        this IHostApplicationBuilder builder,
        string? connectionString,
        IReadOnlyCollection<Assembly> handlerAssemblies)
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

            // Generating also writes the generated .cs to {ContentRoot}/Internal/Generated,
            // and that default survives a Production environment. Not because the profile
            // goes unapplied -- AddWolverine calls options.ReadJasperFxOptions(...), and
            // JasperFxOptions.ReadHostEnvironment, wired through PostConfigure, does set
            // ActiveProfile = Production for a Production host. The setting reads True
            // because JasperFx 2.55.0's Production profile itself initialises
            // SourceCodeWritingEnabled = true: its _development and _production profiles are
            // byte-identical (ResourceAutoCreate = CreateOrUpdate, GeneratedCodeMode =
            // Dynamic, SourceCodeWritingEnabled = true). The library's own XML doc, "false by
            // default in production mode", is the stale part -- not this composition.
            //
            // Setting it here is also what keeps it: ReadJasperFxOptions copies the profile
            // value only while SourceCodeWritingEnabledHasChanged is false, and this setter
            // is what raises that flag.
            //
            // In the container that write cannot succeed: an image built from this branch has
            // /app as drwxr-xr-x root:root with Config.User 1654 and WorkingDir /app. It is
            // not a crash -- the writer catches everything and prints the stack trace -- but
            // a raw UnauthorizedAccessException on stdout, outside Serilog, at first dispatch
            // after every restart is not something to ship and then explain.
            //
            // Nothing is lost at runtime. The files are never read back: Auto loads
            // pre-generated types from the application *assembly*, not from source on disk,
            // so every cold start compiles through Roslyn either way.
            //
            // It does cost the dev loop, and that is the honest half of the trade: this is
            // unconditional, not scoped to the read-only content root that motivates it, so a
            // developer who wants to read generated handler source from a running local host
            // has to edit production code AND break the guard test that holds this setting.
            // `codegen write` is the way out that costs neither -- DynamicCodeBuilder
            // .WriteGeneratedCode writes unconditionally and never consults this setting, so
            // that command would keep working with the line exactly as it stands -- but no
            // entrypoint here offers it yet.
            options.CodeGeneration.SourceCodeWritingEnabled = false;

            // The handler assemblies are the caller's to name, because this one is
            // infrastructure and references no module: its csproj carries no ProjectReference
            // at all. That is convention, not a rule. Architecture rule 4 runs the *forward*
            // direction -- no Fakturenn.Modules.* may reference a Fakturenn.Infrastructure.*
            // -- so an Infrastructure.Messaging -> Modules.Invoices reference would not
            // violate it, and nothing enforces the reverse direction today.
            //
            // Wolverine's own default is to scan the application assembly, and that is THIS
            // assembly -- the one calling UseWolverine -- in the deployed host as much as
            // under a test runner. Measured by running the published host, which logs
            // "Starting Wolverine messaging for application assembly
            // Fakturenn.Infrastructure.Messaging"; the entry assembly is Fakturenn.Web, but
            // WolverineOptions falls through to the calling assembly. No handler will ever
            // live in either, because slices live in Fakturenn.Modules.*, so a module whose
            // assembly is not named here contributes nothing -- silently, with the failure
            // appearing only once a real handler exists. Fakturenn.Web.UnitTests'
            // MessagingCompositionTests is what notices.
            //
            // Above the connection-string guard because discovery is not persistence: a host
            // with nothing configured still dispatches, through in-memory queues, and its
            // handler set must be the same one.
            foreach (Assembly handlerAssembly in handlerAssemblies)
            {
                options.Discovery.IncludeAssembly(handlerAssembly);
            }

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
            // The line below is therefore NOT redundant, whatever the JasperFx profile
            // appears to say. ReadJasperFxOptions fills this setting from
            // ActiveProfile.ResourceAutoCreate whenever it was not set explicitly, and both
            // JasperFx 2.55.0 profiles -- Development and Production alike -- carry
            // ResourceAutoCreate = CreateOrUpdate. Deleting this line does not fall back to
            // a safe default; it falls back to boot-time DDL, which is exactly the invariant
            // MessagingStartupTests exists to defend.
            //
            // It does not make startup tolerant of a missing schema, though. With this set,
            // Wolverine logs "Skipping automatic message storage migration on startup" and
            // then throws from its own explicit check, MessageDatabase.AssertStorageExistsAsync:
            // "The Wolverine message storage for database 'default' is missing or out of date".
            // Note where that is NOT -- an earlier comment here blamed the durability agent's
            // wolverine_nodes query, which is where ContinueOnFailures and Solo crash, not the
            // shipped configuration; neither "wolverine_nodes" nor "messaging" appears anywhere
            // in the resulting exception chain, so anything matching on the failure text must
            // match on Wolverine's own noun, "message storage".
            //
            // A host with a real, unmigrated connection string fails to start -- which is the
            // correct failure shape here: DEPLOYMENT-BASELINE.md's
            // migration Job always runs before a replica serves traffic, so a host that
            // reaches this point with a real connection string and no schema has skipped a
            // required step, and the intended thing to fail is startup itself, not launch
            // and quietly serve traffic with delivery guarantees that turned out imaginary.
            options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
        });
    }
}
