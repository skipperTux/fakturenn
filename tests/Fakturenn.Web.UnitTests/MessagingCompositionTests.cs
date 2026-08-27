using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace Fakturenn.Web.UnitTests;

/// <summary>
/// Host composition, not behaviour. A unit test over a class passes whether or not the
/// class is registered -- which is exactly how the missing AddClaimsPrincipalFactory call
/// hid during E02a until a test asserted the resolved type.
/// </summary>
public sealed class MessagingCompositionTests
{
    /// <summary>
    /// Unreachable on purpose, and never connected to. What the outbox needs is that durable
    /// persistence was <i>configured</i>: <c>EfCoreEnvelopeTransaction</c>'s constructor
    /// throws "This Wolverine application is not using Database backed message persistence"
    /// when it was not, so a host built with no connection string cannot resolve an outbox
    /// however well it is enrolled. Building the host opens no connection, so a port nothing
    /// listens on keeps this a unit test.
    /// </summary>
    private const string ConnectionString =
        "Host=127.0.0.1;Port=1;Database=unreachable;Username=none;Password=none";

    [Fact]
    public void The_invoices_context_is_enrolled_with_the_outbox()
    {
        // Enrolment is per-context, and its absence is silent: an unenrolled context still
        // publishes, non-transactionally, with no error and no warning, so a rollback leaves
        // the row gone and the message sent. What surfaces a broken enrolment is this
        // resolution failing -- IDbContextOutbox<T>'s constructor always sets its Transaction,
        // so no message escapes the transaction by any other route.
        WebApplication app = FakturennWebApplication.Build(
        [
            "--urls",
            "http://127.0.0.1:0",
            $"--ConnectionStrings:Fakturenn={ConnectionString}",
        ]);

        using AsyncServiceScope scope = app.Services.CreateAsyncScope();

        scope.ServiceProvider.GetService<IDbContextOutbox<InvoicesDbContext>>()
            .Should().NotBeNull("every enrolled module context must resolve an outbox");
    }

    [Fact]
    public void Wolverine_discovers_handlers_in_the_module_assemblies()
    {
        // Production discovery is configured and, until E12 publishes something, exercised by
        // nothing else: SetupHostFixture calls Discovery.IncludeAssembly for the *test*
        // assembly, so every integration test would pass on a host that scanned no module at
        // all, and the first real handler would simply never be found.
        //
        // Wolverine's own default is the application assembly, and that is
        // Fakturenn.Infrastructure.Messaging -- the assembly calling UseWolverine -- in the
        // deployed host as well as under a test runner. Measured by running the published
        // host: "Starting Wolverine messaging for application assembly
        // Fakturenn.Infrastructure.Messaging". So the default covers no module either way,
        // and the module assemblies have to be named.
        WebApplication app = FakturennWebApplication.Build(["--urls", "http://127.0.0.1:0"]);

        WolverineOptions options = app.Services.GetRequiredService<WolverineOptions>();

        // WolverineOptions.Assemblies, not Discovery.Assemblies: 6.30.0 keeps that list
        // internal to HandlerDiscovery and exposes it here instead. IncludeAssembly feeds it.
        options.Assemblies.Should().Contain(
            typeof(InvoicesDbContext).Assembly,
            "handlers in a module assembly must be discovered by the host that runs them");
    }

    [Fact]
    public void The_host_does_not_write_generated_source_to_the_content_root()
    {
        // Default is true, and stays true in a Production-environment host: JasperFx's profile
        // documents "false by default in production mode", but nothing in this composition
        // applies that profile. Left on, Wolverine writes generated handler source to
        // {ContentRoot}/Internal/Generated at first dispatch -- into /app, which the image
        // gives the app process no permission to write. The writer swallows the failure and
        // prints the stack trace, so the symptom is an unstructured dump on stdout, outside
        // Serilog, after every restart, and no other test in this repository would see it.
        //
        // Nothing generates in production today, which is exactly why this needs a test: the
        // regression is invisible until E12 ships the first handler.
        WebApplication app = FakturennWebApplication.Build(["--urls", "http://127.0.0.1:0"]);

        WolverineOptions options = app.Services.GetRequiredService<WolverineOptions>();

        options.CodeGeneration.SourceCodeWritingEnabled.Should().BeFalse(
            "the container gives the app process no writable content root");
    }
}
