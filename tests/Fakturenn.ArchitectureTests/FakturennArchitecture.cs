using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
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
    public static readonly Architecture Loaded = new ArchLoader()
        .LoadAssemblies(
            typeof(SharedKernel.Money).Assembly,
            typeof(Infrastructure.Storage.FilesystemBlobWriter).Assembly,
            typeof(Modules.Invoices.Contracts.InvoiceId).Assembly,
            typeof(Modules.Invoices.InvoicesModule).Assembly)
        .Build();

    /// <summary>Every module assembly, contracts included.</summary>
    public static readonly IObjectProvider<IType> Modules =
        Types().That().ResideInAssemblyMatching(@"^Fakturenn\.Modules\..*$")
            .As("module assemblies");

    /// <summary>Module implementation assemblies, contracts excluded.</summary>
    /// <remarks>
    /// <c>ResideInAssemblyMatching</c> matches against the assembly's full CLR name
    /// (e.g. "Fakturenn.Modules.Invoices.Contracts, Version=..., Culture=..., PublicKeyToken=...."),
    /// not the short name. The lookahead therefore terminates on the comma that follows the
    /// short name rather than on <c>$</c> (end of string): anchoring on <c>$</c> would look for
    /// ".Contracts" immediately before the version metadata, never find it, and match every
    /// module assembly including its own contracts.
    /// </remarks>
    public static readonly IObjectProvider<IType> ModuleImplementations =
        Types().That().ResideInAssemblyMatching(@"^Fakturenn\.Modules\.(?!.*\.Contracts,).*$")
            .As("module implementation assemblies");

    public static readonly IObjectProvider<IType> Infrastructure =
        Types().That().ResideInAssemblyMatching(@"^Fakturenn\.Infrastructure\..*$")
            .As("infrastructure assemblies");
}
