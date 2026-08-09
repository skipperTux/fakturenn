using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Fakturenn.ArchitectureTests;

/// <summary>
/// Pins each regex in <see cref="ArchitecturePatterns"/> against a realistic full CLR assembly
/// name (the same format ArchUnitNET matches against -- "Name, Version=..., Culture=...,
/// PublicKeyToken=..."), both a known-good sample it must match and a near-miss it must reject.
/// Without these, a typo in a pattern (e.g. "^MudBlazr.*$") silently turns a
/// <see cref="TechnologyContainmentTests"/> rule's subject or target set empty and the rule
/// passes forever -- exactly how three rules died mid-branch while the suite stayed green.
/// </summary>
public sealed class ArchitecturePatternTests
{
    [Fact]
    public void FakturennWeb_pattern_matches_the_real_assembly_and_rejects_a_near_miss()
    {
        Regex.IsMatch("Fakturenn.Web, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null", ArchitecturePatterns.FakturennWeb)
            .Should().BeTrue("the pattern must match the exact Fakturenn.Web assembly it exists to exclude");

        // A future Fakturenn.Web.Client or Fakturenn.Web.Components (a common Blazor split) is a
        // DIFFERENT assembly and is deliberately NOT covered by this exact-name anchor -- the
        // Task 7 decision recorded in FakturennArchitecture.cs. This pins that choice: loosening
        // the anchor to match it would be a silent policy change, not a bug fix.
        Regex.IsMatch("Fakturenn.Web.Client, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null", ArchitecturePatterns.FakturennWeb)
            .Should().BeFalse("a same-prefix sibling assembly is a different assembly and must not be treated as Fakturenn.Web");
    }

    [Fact]
    public void MudBlazor_pattern_matches_the_real_package_and_rejects_a_near_miss()
    {
        Regex.IsMatch("MudBlazor, Version=8.0.0.0, Culture=neutral, PublicKeyToken=null", ArchitecturePatterns.MudBlazor)
            .Should().BeTrue("the pattern must match the real MudBlazor package name");

        Regex.IsMatch("Blazorise, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", ArchitecturePatterns.MudBlazor)
            .Should().BeFalse("an unrelated Blazor component library must not be caught by the MudBlazor pattern");
    }

    [Fact]
    public void FakturennInfrastructureMail_pattern_matches_the_real_assembly_and_rejects_a_near_miss()
    {
        Regex.IsMatch("Fakturenn.Infrastructure.Mail, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null", ArchitecturePatterns.FakturennInfrastructureMail)
            .Should().BeTrue("the pattern must match the Mail infrastructure assembly itself");

        Regex.IsMatch("Fakturenn.Infrastructure.Mail.Smtp, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null", ArchitecturePatterns.FakturennInfrastructureMail)
            .Should().BeTrue("a Mail-prefixed sibling assembly must also be excluded from the rule's subject set");

        Regex.IsMatch("Fakturenn.Infrastructure.Storage, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null", ArchitecturePatterns.FakturennInfrastructureMail)
            .Should().BeFalse("a different, already-loaded infrastructure assembly must stay in the rule's subject set");
    }

    [Fact]
    public void MimeKitOrMailKit_pattern_matches_the_real_packages_and_rejects_a_near_miss()
    {
        Regex.IsMatch("MimeKit, Version=4.13.0.0, Culture=neutral, PublicKeyToken=null", ArchitecturePatterns.MimeKitOrMailKit)
            .Should().BeTrue("the pattern must match the real MimeKit package name");

        Regex.IsMatch("MailKit, Version=4.13.0.0, Culture=neutral, PublicKeyToken=null", ArchitecturePatterns.MimeKitOrMailKit)
            .Should().BeTrue("the pattern must match the real MailKit package name");

        Regex.IsMatch("SharpMimeTools, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", ArchitecturePatterns.MimeKitOrMailKit)
            .Should().BeFalse("an unrelated MIME library must not be caught by the MimeKit/MailKit pattern");
    }

    [Fact]
    public void FakturennInfrastructureDocuments_pattern_matches_the_real_assembly_and_rejects_a_near_miss()
    {
        Regex.IsMatch("Fakturenn.Infrastructure.Documents, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null", ArchitecturePatterns.FakturennInfrastructureDocuments)
            .Should().BeTrue("the pattern must match the Documents infrastructure assembly itself");

        Regex.IsMatch("Fakturenn.Modules.Invoices, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null", ArchitecturePatterns.FakturennInfrastructureDocuments)
            .Should().BeFalse("a module assembly must not be caught by the Documents infrastructure pattern");
    }

    [Fact]
    public void PdfSharpOrMigraDoc_pattern_matches_the_real_packages_and_rejects_a_near_miss()
    {
        Regex.IsMatch("PdfSharp, Version=6.2.0.0, Culture=neutral, PublicKeyToken=null", ArchitecturePatterns.PdfSharpOrMigraDoc)
            .Should().BeTrue("the pattern must match the real PdfSharp package name");

        Regex.IsMatch("MigraDoc, Version=6.2.0.0, Culture=neutral, PublicKeyToken=null", ArchitecturePatterns.PdfSharpOrMigraDoc)
            .Should().BeTrue("the pattern must match the real MigraDoc package name");

        Regex.IsMatch("Aspose.Pdf, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", ArchitecturePatterns.PdfSharpOrMigraDoc)
            .Should().BeFalse("an unrelated PDF library must not be caught by the PdfSharp/MigraDoc pattern");
    }
}
