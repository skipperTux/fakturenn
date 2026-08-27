using AwesomeAssertions;
using Fakturenn.Infrastructure.DataProtection;
using Fakturenn.Modules.Identity.Persistence;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
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
    // private const Fields

    /// <summary>
    /// Unreachable on purpose, and never connected to. What the outbox needs is that durable
    /// persistence was <i>configured</i>: <c>EfCoreEnvelopeTransaction</c>'s constructor
    /// throws "This Wolverine application is not using Database backed message persistence"
    /// when it was not, so a host built with no connection string cannot resolve an outbox
    /// however well it is enrolled. It is also what puts <c>DatabaseSettings</c> in the
    /// container, which <c>WolverineModelCustomizer</c> resolves before it annotates the
    /// model -- with no connection string even an enrolled context carries no annotation.
    /// Building the host opens no connection, and building a model needs no database, so a
    /// port nothing listens on keeps this a unit test.
    /// </summary>
    private const string ConnectionString =
        "Host=127.0.0.1;Port=1;Database=unreachable;Username=none;Password=none";

    /// <summary>
    /// The annotation <c>WolverineModelCustomizer</c> sets on the model of a context
    /// registered with <c>AddDbContextWithWolverineIntegration</c>, and the one thing
    /// <c>EfCoreEnvelopeTransaction</c> itself branches on. Wolverine keeps the constant
    /// internal, so the string is repeated here.
    /// </summary>
    private const string WolverineEnabledAnnotation = "WolverineEnabled";

    // public Methods
    [Fact]
    public void The_invoices_context_is_enrolled_with_the_outbox()
    {
        // What enrolment does and does not buy, because an earlier version of this comment
        // claimed more than is true. It does NOT make the publish transactional: through
        // IDbContextOutbox<T> that is already settled, because its constructor always sets
        // Transaction, and EfCoreEnvelopeTransaction's unenrolled branch writes the envelope
        // with raw ADO on the context's own connection and CurrentTransaction, beginning one
        // if there is none. What enrolment changes is that the envelope becomes an
        // EF-tracked row saved by the same SaveChangesAsync as the business rows, instead of
        // an eager INSERT inside a transaction Wolverine opened behind the caller's back and
        // only SaveChangesAndFlushMessagesAsync closes.
        //
        // The model annotation, not the outbox resolution. IDbContextOutbox<> is registered as
        // an OPEN generic -- TryAddScoped(typeof(IDbContextOutbox<>), typeof(DbContextOutbox<>))
        // -- and DbContextOutbox<T> needs nothing of T beyond DI resolvability, so once ANY
        // context is enrolled the resolution succeeds for every context in the container.
        // Measured against this host: IDbContextOutbox<IdentityDbContext> and
        // IDbContextOutbox<DataProtectionDbContext> both resolve, and neither is enrolled. A
        // guard asserting resolution would therefore go on passing for the next module after
        // its enrolment was forgotten. The annotation is true only of an enrolled context --
        // see The_unenrolled_contexts_carry_no_wolverine_model_annotation, which is the half
        // that keeps this one honest.
        WebApplication app = FakturennWebApplication.Build(
        [
            "--urls",
            "http://127.0.0.1:0",
            $"--ConnectionStrings:Fakturenn={ConnectionString}",
        ]);

        using AsyncServiceScope scope = app.Services.CreateAsyncScope();

        InvoicesDbContext invoices = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();

        invoices.Model.FindAnnotation(WolverineEnabledAnnotation).Should().NotBeNull(
            "an enrolled context carries Wolverine's envelope entities in its own model");

        // Secondary, and deliberately not the guard: this is the call a slice makes, so it is
        // worth knowing that DbContextOutbox<T>'s own dependencies resolve.
        scope.ServiceProvider.GetService<IDbContextOutbox<InvoicesDbContext>>()
            .Should().NotBeNull("a slice resolves the outbox for its module's context");
    }

    [Fact]
    public void The_unenrolled_contexts_carry_no_wolverine_model_annotation()
    {
        // Two jobs. It proves the assertion above discriminates -- a property every context
        // had would guard nothing -- and it holds the design's deliberate non-enrolments:
        // Identity has no side effect to queue, and data protection is not a business context.
        // If a later epic enrols either, this test is the place that says so out loud.
        WebApplication app = FakturennWebApplication.Build(
        [
            "--urls",
            "http://127.0.0.1:0",
            $"--ConnectionStrings:Fakturenn={ConnectionString}",
        ]);

        using AsyncServiceScope scope = app.Services.CreateAsyncScope();

        IdentityDbContext identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        DataProtectionDbContext dataProtection =
            scope.ServiceProvider.GetRequiredService<DataProtectionDbContext>();

        identity.Model.FindAnnotation(WolverineEnabledAnnotation).Should().BeNull(
            "Identity is deliberately not enrolled");
        dataProtection.Model.FindAnnotation(WolverineEnabledAnnotation).Should().BeNull(
            "data protection is deliberately not enrolled");
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
        // The default is true and stays true on a Production host -- not because the profile
        // goes unapplied, but because JasperFx 2.55.0's Production profile itself initialises
        // SourceCodeWritingEnabled = true. Left on, Wolverine writes generated handler source
        // to {ContentRoot}/Internal/Generated at first dispatch -- into /app, which the image
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
