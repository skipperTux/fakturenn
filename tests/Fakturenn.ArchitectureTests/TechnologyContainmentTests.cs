using ArchUnitNET.Domain;
using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Fakturenn.ArchitectureTests;

/// <summary>
/// Keeps each third-party technology inside the one layer allowed to know about
/// it. The Mail and Documents rules name assemblies that do not exist yet: they
/// are vacuously true today and become binding the moment E11 or E14 creates
/// them. Do not delete a rule because it currently matches nothing.
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
        Types().That().DoNotResideInAssemblyMatching(@"^Fakturenn\.Web(,.*)?$")
            .Should().NotDependOnAny(
                Types().That().ResideInAssemblyMatching(@"^MudBlazor.*$"))
            .Because("MudBlazor is a UI concern and must not leak into modules or infrastructure")
            .Check(FakturennArchitecture.Loaded);
    }

    [Fact]
    public void Only_mail_infrastructure_depends_on_MimeKit_or_MailKit()
    {
        Types().That().DoNotResideInAssemblyMatching(@"^Fakturenn\.Infrastructure\.Mail.*$")
            .Should().NotDependOnAny(
                Types().That().ResideInAssemblyMatching(@"^(MimeKit|MailKit).*$"))
            .Because("MIME composition and signing belong behind the Mail module's contracts")
            .Check(FakturennArchitecture.Loaded);
    }

    [Fact]
    public void Only_document_infrastructure_depends_on_PdfSharp_or_MigraDoc()
    {
        Types().That().DoNotResideInAssemblyMatching(@"^Fakturenn\.Infrastructure\.Documents.*$")
            .Should().NotDependOnAny(
                Types().That().ResideInAssemblyMatching(@"^(PdfSharp|MigraDoc).*$"))
            .Because("rendering belongs behind the Documents module's rendering contracts")
            .Check(FakturennArchitecture.Loaded);
    }
}
