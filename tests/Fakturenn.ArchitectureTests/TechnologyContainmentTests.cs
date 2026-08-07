using ArchUnitNET.Domain;
using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Fakturenn.ArchitectureTests;

/// <summary>
/// Keeps each third-party technology inside the one layer allowed to know about
/// it. The Mail and Documents rules name assemblies that do not exist yet: they
/// are demonstrated vacuously true today (see the fix-round-1 proof in
/// task-6-report.md, which deliberately made Fakturenn.Modules.Invoices depend
/// on MimeKit and watched Only_mail_infrastructure_depends_on_MimeKit_or_MailKit
/// fail) and become binding the moment E11 or E14 creates those assemblies. Do
/// not delete a rule because it currently matches nothing.
/// </summary>
public sealed class TechnologyContainmentTests
{
    [Fact]
    public void Only_the_web_assembly_depends_on_MudBlazor()
    {
        // DoNotResideInAssemblyMatching, not the exact-name DoNotResideInAssembly: the exact
        // overload compares against the assembly's full CLR name (including Version=...,
        // Culture=..., PublicKeyToken=...) and so would never match "Fakturenn.Web" literally,
        // even once that assembly exists.
        //
        // NotDependOnAnyTypesThat().ResideInAssemblyMatching(...), not
        // NotDependOnAny(Types().That().ResideInAssemblyMatching(...)): the latter first
        // materializes the target set by calling GetObjects() against the LOADED architecture,
        // which can only ever contain types from assemblies passed to LoadAssemblies. MudBlazor,
        // MimeKit, MailKit, PDFsharp and MigraDoc are never loaded, so that target set is always
        // empty and NotDependOnAny(<empty>) passes unconditionally -- dead, not vacuous, and it
        // would stay dead forever, even after Fakturenn.Web starts referencing MudBlazor.
        // NotDependOnAnyTypesThat() instead evaluates the predicate against each dependency's
        // TARGET type directly, which is resolved from the referencing assembly's metadata and
        // is not limited to what LoadAssemblies loaded. Proven empirically for rule 2 in
        // task-6-report.md: the same violation passes the first form (0 target types resolved)
        // and fails the second (1 violation reported).
        Types().That().DoNotResideInAssemblyMatching(@"^Fakturenn\.Web(,.*)?$")
            .Should().NotDependOnAnyTypesThat().ResideInAssemblyMatching(@"^MudBlazor.*$")
            .Because("MudBlazor is a UI concern and must not leak into modules or infrastructure")
            .Check(FakturennArchitecture.Loaded);
    }

    [Fact]
    public void Only_mail_infrastructure_depends_on_MimeKit_or_MailKit()
    {
        Types().That().DoNotResideInAssemblyMatching(@"^Fakturenn\.Infrastructure\.Mail.*$")
            .Should().NotDependOnAnyTypesThat().ResideInAssemblyMatching(@"^(MimeKit|MailKit).*$")
            .Because("MIME composition and signing belong behind the Mail module's contracts")
            .Check(FakturennArchitecture.Loaded);
    }

    [Fact]
    public void Only_document_infrastructure_depends_on_PdfSharp_or_MigraDoc()
    {
        Types().That().DoNotResideInAssemblyMatching(@"^Fakturenn\.Infrastructure\.Documents.*$")
            .Should().NotDependOnAnyTypesThat().ResideInAssemblyMatching(@"^(PdfSharp|MigraDoc).*$")
            .Because("rendering belongs behind the Documents module's rendering contracts")
            .Check(FakturennArchitecture.Loaded);
    }
}
