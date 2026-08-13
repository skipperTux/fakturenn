using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;
using AwesomeAssertions.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Fakturenn.Web.UnitTests;

/// <summary>
/// Guards the two resource files against the three ways a translation quietly stops being
/// one: a key that exists only in English, a "translation" that is the English string
/// copied across, and a key the code asks for that no file answers.
/// </summary>
public sealed partial class SharedResourceTests
{
    // private static readonly Fields

    /// <summary>
    /// Keys whose German value is deliberately identical to the English one.
    /// <para>
    /// This set is the whole reason the identical-value check below is worth running. A
    /// parity test that compares only key <i>names</i> passes just as happily when
    /// <c>SharedResource.de.resx</c> is a byte copy of the English file — which is exactly
    /// what a rushed "add the German resources" change produces. Requiring every matching
    /// value to be listed here turns each one into a decision somebody made rather than a
    /// gap nobody noticed.
    /// </para>
    /// <para>
    /// <b>It is empty today, and that is a finding, not an oversight.</b> Every one of the
    /// 85 keys currently differs between the two languages. The entries this set exists to
    /// hold are things like a bare product name or a unit symbol; the closest current
    /// candidates — "Email"/"E-Mail", "Authenticator code"/"Code aus der Authenticator-App"
    /// — all differ. Add a key here only with a comment saying why the German is correctly
    /// the same word.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> _sameInBothLanguages = new(StringComparer.Ordinal);

    /// <summary>
    /// Every <c>Localizer["Key"]</c> / <c>localizer["Key"]</c> lookup in the web project's
    /// own source, whatever the injected instance is called and whatever follows the key.
    /// </summary>
    [GeneratedRegex(@"[Ll]ocalizer\[\s*""([^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex LocalizerLookup { get; }

    // public Methods

    [Fact]
    public void Every_english_resource_key_has_a_german_translation()
    {
        IReadOnlyDictionary<string, string> english = Read("SharedResource.resx");
        IReadOnlyDictionary<string, string> german = Read("SharedResource.de.resx");

        using AssertionScope scope = new();

        english.Keys.Except(german.Keys).Should()
            .BeEmpty("a key missing from the German file silently falls back to English, "
                + "which reads as a working application in review and as a half-translated "
                + "one to a German user");

        german.Keys.Except(english.Keys).Should()
            .BeEmpty("a German key with no English source is a leftover from a renamed key");
    }

    [Fact]
    public void No_german_value_is_the_english_value_copied_across()
    {
        IReadOnlyDictionary<string, string> english = Read("SharedResource.resx");
        IReadOnlyDictionary<string, string> german = Read("SharedResource.de.resx");

        IEnumerable<string> untranslated = english
            .Where(entry =>
                german.TryGetValue(entry.Key, out string? translated)
                && string.Equals(translated, entry.Value, StringComparison.Ordinal)
                && !_sameInBothLanguages.Contains(entry.Key))
            .Select(entry => entry.Key);

        untranslated.Should()
            .BeEmpty("an identical value is an untranslated one unless somebody decided "
                + "otherwise; record the decision in _sameInBothLanguages with a reason");
    }

    [Fact]
    public void Every_key_the_code_asks_for_exists_in_both_files()
    {
        // The failure this catches has no compiler error and no runtime exception behind
        // it: IStringLocalizer answers a missing key with the key name, so a typo renders
        // "Account_Login_Titel" to the user on a page that otherwise looks finished.
        IReadOnlyDictionary<string, string> english = Read("SharedResource.resx");
        IReadOnlyDictionary<string, string> german = Read("SharedResource.de.resx");
        IReadOnlySet<string> requested = KeysRequestedBySource();

        using AssertionScope scope = new();

        requested.Should().NotBeEmpty(
            "the scan must actually find the lookups it claims to check; an empty result "
            + "means the pattern or the search root is wrong, not that the code is clean");

        requested.Except(english.Keys).Should().BeEmpty("every key the code asks for must exist");
        requested.Except(german.Keys).Should().BeEmpty("every key the code asks for must be translated");
    }

    [Fact]
    public void Every_resource_key_is_asked_for_by_the_code()
    {
        // The other direction. A key nobody reads is either a page that was never
        // localized after all, or dead weight a translator is still being asked to
        // maintain.
        IReadOnlyDictionary<string, string> english = Read("SharedResource.resx");

        english.Keys.Except(KeysRequestedBySource()).Should()
            .BeEmpty("an unused key is either a missed call site or dead weight");
    }

    [Fact]
    public void The_identity_error_describer_speaks_the_request_language()
    {
        // Registration, not behaviour, is what fails silently here: without the
        // AddErrorDescriber line every page in this epic is German with Identity's English
        // password rules printed in the middle of it, and every other test stays green.
        WebApplication app = FakturennWebApplication.Build(["--urls", "http://127.0.0.1:0"]);
        using IServiceScope scope = app.Services.CreateScope();

        IdentityErrorDescriber describer =
            scope.ServiceProvider.GetRequiredService<IdentityErrorDescriber>();

        describer.Should().BeOfType<LocalizedIdentityErrorDescriber>();

        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            // What UseRequestLocalization does per request, done by hand: the localizer
            // reads the culture at lookup time, which is what lets one scoped describer
            // answer every language.
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");

            describer.PasswordTooShort(12).Description.Should()
                .Be("Passwörter müssen mindestens 12 Zeichen lang sein.");

            // The code is the untranslated base-class name. POST /account/setup branches on
            // it, so translating a code would break a control-flow decision, not a message.
            describer.DuplicateUserName("a@b.test").Code.Should().Be("DuplicateUserName");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    // private Methods

    private static Dictionary<string, string> Read(string fileName)
    {
        string path = Path.Combine(RepositoryRoot(), "src", "Fakturenn.Web", "Resources", fileName);

        File.Exists(path).Should().BeTrue($"{path} must exist");

        return XDocument.Load(path).Root!
            .Elements("data")
            .ToDictionary(
                data => data.Attribute("name")!.Value,
                data => data.Element("value")!.Value,
                StringComparer.Ordinal);
    }

    /// <summary>
    /// Reads the web project's own sources rather than its compiled assembly, because the
    /// key is a string literal: nothing about it survives compilation in a form a
    /// reflection-based check could compare against the resource files.
    /// </summary>
    private static IReadOnlySet<string> KeysRequestedBySource()
    {
        string project = Path.Combine(RepositoryRoot(), "src", "Fakturenn.Web");

        IEnumerable<string> sources = Directory
            .EnumerateFiles(project, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".razor", StringComparison.Ordinal)
                || file.EndsWith(".cs", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        return sources
            .SelectMany(file => LocalizerLookup.Matches(File.ReadAllText(file)).Cast<Match>())
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The repository root, from this file's own compile-time path — the same mechanism
    /// <c>ModuleBoundaryTests</c> uses, and it depends on the repository deliberately not
    /// setting <c>DeterministicSourcePaths</c> or <c>ContinuousIntegrationBuild</c>. See
    /// IMPLEMENTATION-NOTES.
    /// </summary>
    private static string RepositoryRoot([CallerFilePath] string sourceFilePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));
}
