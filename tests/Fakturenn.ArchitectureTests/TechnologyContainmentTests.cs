using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Fakturenn.ArchitectureTests;

/// <summary>
/// Keeps each third-party technology inside the one layer allowed to know about
/// it. All three rules here are live and binding NOW, not vacuous: each subject
/// selector is <c>DoNotResideInAssemblyMatching(&lt;the owning assembly's
/// pattern&gt;)</c> -- "every assembly that is NOT the owner" -- which today
/// resolves to all five loaded assemblies, including the two whose owning
/// assembly (Mail, Documents) does not exist yet. Proven empirically in
/// task-6-report.md's fix-round-1: deliberately making Fakturenn.Modules.Invoices
/// depend on real MimeKit made
/// Only_mail_infrastructure_depends_on_MimeKit_or_MailKit fail. When
/// Fakturenn.Infrastructure.Mail* or .Documents* eventually appears, the
/// corresponding rule does not newly switch on -- it gets narrower, carving out
/// an exemption for the one assembly now allowed to reference the library. (The
/// suite's genuinely vacuous rules -- ModuleBoundaryTests' cross-module and
/// no-cycle checks -- live elsewhere, because they need a second
/// Fakturenn.Modules.* assembly to have anything to compare.)
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
        Types().That().DoNotResideInAssemblyMatching(ArchitecturePatterns.FakturennWeb)
            .Should().NotDependOnAnyTypesThat().ResideInAssemblyMatching(ArchitecturePatterns.MudBlazor)
            .Because("MudBlazor is a UI concern and must not leak into modules or infrastructure")
            .Check(FakturennArchitecture.Loaded);
    }

    [Fact]
    public void Only_mail_infrastructure_depends_on_MimeKit_or_MailKit()
    {
        Types().That().DoNotResideInAssemblyMatching(ArchitecturePatterns.FakturennInfrastructureMail)
            .Should().NotDependOnAnyTypesThat().ResideInAssemblyMatching(ArchitecturePatterns.MimeKitOrMailKit)
            .Because("MIME composition and signing belong behind the Mail module's contracts")
            .Check(FakturennArchitecture.Loaded);
    }

    [Fact]
    public void Only_document_infrastructure_depends_on_PdfSharp_or_MigraDoc()
    {
        Types().That().DoNotResideInAssemblyMatching(ArchitecturePatterns.FakturennInfrastructureDocuments)
            .Should().NotDependOnAnyTypesThat().ResideInAssemblyMatching(ArchitecturePatterns.PdfSharpOrMigraDoc)
            .Because("rendering belongs behind the Documents module's rendering contracts")
            .Check(FakturennArchitecture.Loaded);
    }
}
