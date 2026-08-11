using System.Runtime.CompilerServices;
using System.Xml.Linq;
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent.Slices;
using ArchUnitNET.xUnitV3;
using AwesomeAssertions;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Fakturenn.ArchitectureTests;

public sealed class ModuleBoundaryTests
{
    // public Methods
    [Fact]
    public void The_architecture_contains_the_assemblies_the_rules_govern()
    {
        // Without this, every rule below would pass vacuously if assembly
        // loading broke, and the suite would go green while enforcing nothing.
        IEnumerable<string> assemblies = FakturennArchitecture.Loaded.Assemblies
            .Select(assembly => assembly.Name.Split(',')[0]);

        assemblies.Should().Contain([
            "Fakturenn.SharedKernel",
            "Fakturenn.Infrastructure.Storage",
            "Fakturenn.Infrastructure.Persistence",
            "Fakturenn.Modules.Invoices",
            "Fakturenn.Modules.Invoices.Contracts",
            "Fakturenn.Modules.Identity",
            "Fakturenn.Modules.Identity.Contracts",
        ]);

        // The assembly list alone is not enough: a typo in any of FakturennArchitecture's regexes
        // would silently turn Modules, ModuleImplementations or Infrastructure into an empty
        // object provider, and every rule that depends on it (4, 5, 6) would then pass vacuously
        // with the suite green. Assert each provider actually resolves to at least one real type
        // against the loaded architecture.
        FakturennArchitecture.Modules.GetObjects(FakturennArchitecture.Loaded)
            .Should().NotBeEmpty("the Modules provider's regex must match at least one loaded type");
        FakturennArchitecture.ModuleImplementations.GetObjects(FakturennArchitecture.Loaded)
            .Should().NotBeEmpty("the ModuleImplementations provider's regex must match at least one loaded type");
        FakturennArchitecture.Infrastructure.GetObjects(FakturennArchitecture.Loaded)
            .Should().NotBeEmpty("the Infrastructure provider's regex must match at least one loaded type");
    }

    [Fact]
    public void The_loader_omits_no_assembly_declared_under_src_in_the_solution()
    {
        // FakturennArchitecture.Loaded is built from a hard-coded list of typeof(...).Assembly
        // lines. A later epic that adds e.g. Fakturenn.Modules.Payments but forgets to add its
        // line here would silently exempt that module from rules 4, 5 and 6 forever, with
        // nothing going red -- exactly the failure mode this suite exists to prevent, and the
        // anti-vacuity guard above cannot catch it because it only knows today's four names.
        // Cross-check the loader against an independent source of truth instead: Fakturenn.slnx's
        // /src/ folder, which every project must already be listed under to build at all.
        string solutionPath = Path.Combine(RepositoryRoot(), "Fakturenn.slnx");
        File.Exists(solutionPath).Should().BeTrue($"the solution file must exist at {solutionPath}");

        XDocument solution = XDocument.Load(solutionPath);
        IEnumerable<string> expectedAssemblyNames = solution.Descendants("Folder")
            .Where(folder => (string?)folder.Attribute("Name") == "/src/")
            .Descendants("Project")
            .Select(project => Path.GetFileNameWithoutExtension((string)project.Attribute("Path")!));

        IEnumerable<string> loadedAssemblyNames = FakturennArchitecture.Loaded.Assemblies
            .Select(assembly => assembly.Name);

        loadedAssemblyNames.Should().BeEquivalentTo(
            expectedAssemblyNames,
            "every project under Fakturenn.slnx's /src/ folder needs a matching typeof(...).Assembly "
                + "line in FakturennArchitecture.Loaded, or the architecture rules silently stop "
                + "governing it");
    }

    [Fact]
    public void No_module_depends_on_infrastructure()
    {
        // Infrastructure implements module-owned interfaces, never the reverse.
        // This is also what keeps E-Invoice-EU adapter types out of the domain.
        Types().That().Are(FakturennArchitecture.Modules)
            .Should().NotDependOnAny(FakturennArchitecture.Infrastructure)
            .Because("MODULE-OWNERSHIP.md fixes the direction UI -> slices -> module contracts -> infrastructure")
            .Check(FakturennArchitecture.Loaded);
    }

    [Fact]
    public void No_module_depends_on_another_modules_implementation_assembly()
    {
        // Not expressed as the brief's
        // Types().That().Are(Modules).Should().NotDependOnAny(ModuleImplementations).Check(...):
        // ArchUnitNET records a type's access to its own field -- any constructor that assigns
        // `this._field = value`, or a static field initializer -- as a dependency from that
        // type to itself. Because Modules and ModuleImplementations overlap for every
        // implementation assembly, that self-reference reads as a violation of this rule for
        // any class with a field initializer, i.e. almost every non-trivial class. Verified
        // empirically: a plain `sealed class C { public C(int v) { _v = v; } }` fails the
        // fluent rule with "C does depend on C", even though C never references another
        // module. The check below walks the same Dependencies collection ArchUnitNET's own
        // NotDependOnAny uses, but compares module names (stripping the ".Contracts" suffix)
        // rather than raw type identity, so a dependency within one module -- including on
        // itself -- is correctly not a violation, while a genuine dependency on another
        // module's implementation still is. Independently re-verified against a second, genuine
        // module built for this purpose (see task-6-report.md, fix round 1): it correctly caught
        // cross-module dependencies expressed as a field, a generic argument, an inheritance
        // relationship, a method parameter, a method-body local, an attribute and a generic
        // constraint, with no false positive on a self- or own-.Contracts-reference.
        IReadOnlyCollection<IType> modules =
            FakturennArchitecture.Modules.GetObjects(FakturennArchitecture.Loaded).ToList();
        IReadOnlyCollection<IType> moduleImplementations =
            FakturennArchitecture.ModuleImplementations.GetObjects(FakturennArchitecture.Loaded).ToList();

        List<string> violations = modules
            .SelectMany(origin => origin.Dependencies)
            .Where(dependency => moduleImplementations.Contains(dependency.Target))
            .Where(dependency => ModuleNameOf(dependency.Origin) != ModuleNameOf(dependency.Target))
            .Select(dependency => $"{dependency.Origin.FullName} -> {dependency.Target.FullName}")
            .Distinct()
            .ToList();

        violations.Should().BeEmpty(
            "cross-module access goes through Fakturenn.Modules.<Name>.Contracts, never the owner's entities");
    }

    [Fact]
    public void There_are_no_dependency_cycles_between_modules()
    {
        SliceRuleDefinition.Slices()
            .Matching("Fakturenn.Modules.(*)")
            .Should().BeFreeOfCycles()
            .Check(FakturennArchitecture.Loaded);
    }

    // private Methods
    private static string RepositoryRoot([CallerFilePath] string sourceFilePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));

    /// <summary>
    /// Strips a trailing ".Contracts" from a module implementation assembly's short name, so a
    /// dependency's origin and target can be compared by owning module rather than by raw
    /// assembly identity.
    /// </summary>
    /// <remarks>
    /// Safe only because every caller pre-filters its input to types drawn from
    /// <see cref="FakturennArchitecture.Loaded"/>'s <c>Modules</c> or <c>ModuleImplementations</c>
    /// providers. <c>IType.Assembly.Name</c> returns the short assembly name for a type belonging
    /// to a <em>loaded</em> assembly, but falls back to the full CLR name (with
    /// ", Version=..., Culture=..., PublicKeyToken=...") for a type ArchUnitNET only knows about
    /// as an unloaded dependency target -- verified empirically against MimeKit and
    /// System.Private.CoreLib types in task-6-report.md, fix round 1. A future reorder that calls
    /// this on an unfiltered or unloaded type would silently stop matching the ".Contracts" suffix.
    /// </remarks>
    private static string ModuleNameOf(IType type) =>
        type.Assembly.Name.EndsWith(".Contracts", StringComparison.Ordinal)
            ? type.Assembly.Name[..^".Contracts".Length]
            : type.Assembly.Name;
}
