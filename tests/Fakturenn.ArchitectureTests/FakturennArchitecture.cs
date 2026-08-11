using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Fakturenn.ArchitectureTests;

/// <summary>
/// Loaded once per run because building the type graph is the expensive part.
/// The providers below match on assembly-name patterns rather than a list of
/// assemblies, so a module added by a later epic is governed the moment it
/// exists and needs no new rule.
/// </summary>
public static class FakturennArchitecture
{
    // Every src/ assembly needs a line here. There is no compiler error for a forgotten one --
    // it silently exempts that assembly from every rule below, because a type that was never
    // loaded cannot appear as a rule violation. ModuleBoundaryTests.
    // The_loader_omits_no_assembly_declared_under_src_in_the_solution cross-checks this list
    // against Fakturenn.slnx's /src/ folder specifically to catch that mistake.
    public static readonly Architecture Loaded = new ArchLoader()
        .LoadAssemblies(
            typeof(SharedKernel.Money).Assembly,
            typeof(Infrastructure.Storage.FilesystemBlobWriter).Assembly,
            typeof(Modules.Invoices.Contracts.InvoiceId).Assembly,
            typeof(Modules.Invoices.InvoicesModule).Assembly,
            typeof(Modules.Identity.Contracts.UserId).Assembly,
            typeof(Modules.Identity.IdentityModule).Assembly,
            typeof(Infrastructure.Persistence.AuditSaveChangesInterceptor).Assembly,
            typeof(Web.FakturennWebApplication).Assembly)
        .Build();

    /// <summary>Every module assembly, contracts included.</summary>
    public static readonly IObjectProvider<IType> Modules =
        Types().That().ResideInAssemblyMatching(@"^Fakturenn\.Modules\..*$")
            .As("module assemblies");

    /// <summary>Module implementation assemblies, contracts excluded.</summary>
    /// <remarks>
    /// <c>ResideInAssemblyMatching</c> matches against the assembly's full CLR name
    /// (e.g. "Fakturenn.Modules.Invoices.Contracts, Version=..., Culture=..., PublicKeyToken=...."),
    /// not the short name. The lookahead therefore terminates on <c>(,|$)</c> -- the comma that
    /// starts the version metadata in a loaded assembly's full name, OR true end of string, so the
    /// exclusion still works if a dependency target's name is ever reported without the metadata
    /// suffix -- rather than on a bare <c>$</c>: anchoring on <c>$</c> alone would look for
    /// ".Contracts" immediately before the version metadata, never find it, and match every
    /// module assembly including its own contracts.
    /// </remarks>
    public static readonly IObjectProvider<IType> ModuleImplementations =
        Types().That().ResideInAssemblyMatching(@"^Fakturenn\.Modules\.(?!.*\.Contracts(,|$)).*$")
            .As("module implementation assemblies");

    public static readonly IObjectProvider<IType> Infrastructure =
        Types().That().ResideInAssemblyMatching(@"^Fakturenn\.Infrastructure\..*$")
            .As("infrastructure assemblies");
}
