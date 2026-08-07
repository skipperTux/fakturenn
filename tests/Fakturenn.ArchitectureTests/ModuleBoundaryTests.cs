using ArchUnitNET.Domain;
using ArchUnitNET.Fluent.Slices;
using ArchUnitNET.xUnitV3;
using AwesomeAssertions;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Fakturenn.ArchitectureTests;

public sealed class ModuleBoundaryTests
{
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
            "Fakturenn.Modules.Invoices",
            "Fakturenn.Modules.Invoices.Contracts",
        ]);
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
        // module's implementation still is.
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

    private static string ModuleNameOf(IType type) =>
        type.Assembly.Name.EndsWith(".Contracts", StringComparison.Ordinal)
            ? type.Assembly.Name[..^".Contracts".Length]
            : type.Assembly.Name;

    [Fact]
    public void There_are_no_dependency_cycles_between_modules()
    {
        SliceRuleDefinition.Slices()
            .Matching("Fakturenn.Modules.(*)")
            .Should().BeFreeOfCycles()
            .Check(FakturennArchitecture.Loaded);
    }
}
