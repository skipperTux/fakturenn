using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// <c>POST /account/setup</c> is the only unauthenticated endpoint in the system that
/// mints an administrator. Every test here drives it over HTTP through the real
/// pipeline; none of them constructs the handler or a <c>UserManager</c> directly.
/// </summary>
[Collection(RealHost.Name)]
public sealed partial class SetupEndpointTests(SetupHostFixture host)
{
    private const string ValidPassword = "Korrekt-Pferd-42";

    [Fact]
    public async Task Posting_to_an_empty_instance_creates_one_administrator_who_must_enrol_totp()
    {
        await host.ResetUsersAsync(TestContext.Current.CancellationToken);

        using HttpClient client = host.CreateClient(new CookieContainer());
        using HttpResponseMessage response =
            await PostSetupAsync(client, await SetupTokenAsync(client), "first@example.test");

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location?.OriginalString.Should().Be("/account/login");

        await using IdentityDbContext context = host.CreateIdentityContext();

        ApplicationUser user = await context.Users.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        user.Email.Should().Be("first@example.test");
        user.MustEnrolTotp.Should().BeTrue("nothing else forces the second factor to be enrolled");
        user.MustChangePassword.Should().BeFalse("this user chose the password themselves");

        Guid administratorRoleId = await AdministratorRoleIdAsync(context);

        UserRole assignment = await context.UserRoles.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        assignment.UserId.Should().Be(user.Id);
        assignment.RoleId.Should().Be(administratorRoleId);
    }

    [Fact]
    public async Task A_second_post_after_configuration_creates_nothing()
    {
        await host.ResetUsersAsync(TestContext.Current.CancellationToken);

        using HttpClient client = host.CreateClient(new CookieContainer());
        string token = await SetupTokenAsync(client);

        using (HttpResponseMessage first = await PostSetupAsync(client, token, "owner@example.test"))
        {
            first.StatusCode.Should().Be(HttpStatusCode.Found);
        }

        using HttpResponseMessage second = await PostSetupAsync(client, token, "intruder@example.test");

        // The closed-path answer, not an unhandled exception: an endpoint that 500s on
        // the second post is still leaking that it did work before it failed.
        second.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using IdentityDbContext context = host.CreateIdentityContext();

        List<string?> emails = await context.Users.AsNoTracking()
            .Select(user => user.Email)
            .ToListAsync(TestContext.Current.CancellationToken);
        emails.Should().ContainSingle().Which.Should().Be("owner@example.test");

        int assignments = await context.UserRoles.AsNoTracking()
            .CountAsync(TestContext.Current.CancellationToken);
        assignments.Should().Be(1, "no second administrator may be granted the role either");
    }

    [Fact]
    public async Task Concurrent_posts_against_an_empty_instance_produce_exactly_one_user()
    {
        // The race the endpoint's own comments describe: the count check and the insert
        // are not atomic. The window is not theoretical -- password hashing sits between
        // them -- so all of these requests are inside it at the same time.
        await host.ResetUsersAsync(TestContext.Current.CancellationToken);

        const int Racers = 4;

        using HttpClient client = host.CreateClient(new CookieContainer());
        string token = await SetupTokenAsync(client);
        using Barrier startTogether = new(Racers);

        Task<HttpResponseMessage>[] posts = [.. Enumerable.Range(0, Racers).Select(index =>
            Task.Run(
                async () =>
                {
                    // Released as one, so the count checks overlap rather than queueing.
                    startTogether.SignalAndWait(TestContext.Current.CancellationToken);

                    return await PostSetupAsync(client, token, $"racer-{index}@example.test");
                },
                TestContext.Current.CancellationToken))];

        HttpResponseMessage[] responses = await Task.WhenAll(posts);
        foreach (HttpResponseMessage response in responses)
        {
            response.Dispose();
        }

        await using IdentityDbContext context = host.CreateIdentityContext();

        List<string?> emails = await context.Users.AsNoTracking()
            .Select(user => user.Email)
            .OrderBy(email => email)
            .ToListAsync(TestContext.Current.CancellationToken);

        emails.Should().ContainSingle(
            "a first-run endpoint that mints an administrator must produce one administrator, "
            + $"but it produced: {string.Join(", ", emails)}");
    }

    [Fact]
    public async Task A_password_the_policy_rejects_creates_no_user()
    {
        // Proves CreateAsync runs the configured validators. Without this, the page's
        // Required attributes would be the only gate, and a direct post skips those.
        await host.ResetUsersAsync(TestContext.Current.CancellationToken);

        using HttpClient client = host.CreateClient(new CookieContainer());
        using HttpResponseMessage response = await PostSetupAsync(
            client, await SetupTokenAsync(client), "weak@example.test", password: "short");

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location?.OriginalString.Should().StartWith("/setup?error=");

        await using IdentityDbContext context = host.CreateIdentityContext();

        bool anyUser = await context.Users.AsNoTracking()
            .AnyAsync(TestContext.Current.CancellationToken);
        anyUser.Should().BeFalse();
    }

    [Fact]
    public async Task The_endpoint_refuses_when_users_exist_even_though_the_page_was_never_visited()
    {
        // The page's _alreadyConfigured check is a redirect for humans. This request
        // never renders it: the user is planted straight into the database and the post
        // goes to the endpoint. If the server-side guard were deleted, the page would
        // still redirect and this would still create an administrator.
        await host.ResetUsersAsync(TestContext.Current.CancellationToken);

        await using (IdentityDbContext seed = host.CreateIdentityContext())
        {
            seed.Users.Add(new ApplicationUser
            {
                Id = Guid.CreateVersion7(),
                UserName = "planted@example.test",
                NormalizedUserName = "PLANTED@EXAMPLE.TEST",
                Email = "planted@example.test",
                NormalizedEmail = "PLANTED@EXAMPLE.TEST",
                DisplayName = "Planted",
                SecurityStamp = Guid.NewGuid().ToString("N"),
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // The token comes from a page that is not /setup, so the setup page really is never
        // rendered for this caller — which is the whole point of the test.
        using HttpClient client = host.CreateClient(new CookieContainer());
        string token = await AntiforgeryHelper.TokenFromAsync(client, AntiforgeryHelper.AnonymousTokenPage);

        using HttpResponseMessage response = await PostSetupAsync(client, token, "bypass@example.test");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using IdentityDbContext context = host.CreateIdentityContext();

        List<string?> emails = await context.Users.AsNoTracking()
            .Select(user => user.Email)
            .ToListAsync(TestContext.Current.CancellationToken);
        emails.Should().ContainSingle().Which.Should().Be("planted@example.test");
    }

    [Fact]
    public async Task A_post_without_an_antiforgery_token_creates_nothing()
    {
        // /account/setup is NOT exempt from antiforgery, and this is the assertion that
        // says so. An earlier disposition accepted the exemption on the grounds that an
        // attacker who can reach an unconfigured instance can simply post to it -- true,
        // and beside the point for the attacker who cannot reach it and uses a victim's
        // browser to claim the instance with a password of their choosing.
        await host.ResetUsersAsync(TestContext.Current.CancellationToken);

        using HttpClient client = host.CreateClient(new CookieContainer());
        using HttpResponseMessage response = await AntiforgeryHelper.PostWithoutTokenAsync(
            client,
            "/account/setup",
            ("email", "forged@example.test"),
            ("displayName", "Forged"),
            ("password", ValidPassword));

        // A redirect back to the form, rather than the unhandled BadHttpRequestException
        // RequestDelegateFactory used to throw -- a 400 with a developer exception page under
        // Development, a bare 400 under Production, logged as a 500 either way. What matters
        // for this test is the half that has not changed: the post is refused and no
        // administrator exists afterwards.
        response.StatusCode.Should().Be(
            HttpStatusCode.Found, "a first-run post without a token must never mint an administrator");
        response.Headers.Location?.OriginalString.Should().Be("/setup?error=expired");

        await using IdentityDbContext context = host.CreateIdentityContext();

        bool anyUser = await context.Users.AsNoTracking()
            .AnyAsync(TestContext.Current.CancellationToken);
        anyUser.Should().BeFalse();
    }

    [Fact]
    public async Task A_password_the_policy_refuses_hands_back_everything_except_the_password()
    {
        // The first form anybody meets, filled in by somebody who has no account yet and no
        // way to recover one. It used to answer a refused password by discarding the address
        // and the display name as well, so a policy the operator had not read cost them the
        // whole form every time.
        await host.ResetUsersAsync(TestContext.Current.CancellationToken);

        using HttpClient client = host.CreateClient(new CookieContainer());

        const string TooShort = "Kurz-9";

        using HttpResponseMessage posted = await AntiforgeryHelper.PostWithTokenAsync(
            client,
            await SetupTokenAsync(client),
            "/account/setup",
            ("email", "typed@example.test"),
            ("displayName", "Getippter Name"),
            ("password", TooShort));

        posted.StatusCode.Should().Be(HttpStatusCode.Found);

        string location = posted.Headers.Location!.OriginalString;

        location.Should().StartWith("/setup?error=");
        location.Should().Contain("&email=typed%40example.test");
        location.Should().Contain("&displayName=Getippter%20Name");
        location.Should().NotContain(
            "Kurz", "a password must never travel in a URL -- it lands in browser history and "
            + "in every reverse proxy's access log");

        using HttpResponseMessage page = await client.GetAsync(
            new Uri(location, UriKind.Relative), TestContext.Current.CancellationToken);

        string html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        html.Should().Contain("value=\"typed@example.test\"");
        html.Should().Contain("value=\"Getippter Name\"");
        PasswordInput(html).Should().NotContain(
            "value=", "the password box must come back empty, whatever else is refilled");

        // And the message names the actual rule, not a generic refusal: Task 17 localized
        // IdentityErrorDescriber precisely so this sentence is both real and translated.
        html.Should().Contain("at least 12 characters");

        bool anyUser = await AnyUserAsync();
        anyUser.Should().BeFalse("a refused password must create nothing");
    }

    /// <summary>
    /// The single <c>&lt;input&gt;</c> element MudBlazor renders for the password field.
    /// Isolating it matters: the page has three inputs and asserting "no value= anywhere"
    /// would fail on the two that are supposed to carry one.
    /// </summary>
    private static string PasswordInput(string html) =>
        PasswordInputPattern().Match(html).Value;

    [GeneratedRegex("""<input[^>]*name="password"[^>]*>""")]
    private static partial Regex PasswordInputPattern();

    private async Task<bool> AnyUserAsync()
    {
        await using IdentityDbContext context = host.CreateIdentityContext();

        return await context.Users.AsNoTracking().AnyAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The token <c>/setup</c> renders while the instance is empty, which is the state
    /// every test in this class arranges before it posts.
    /// <para>
    /// Fetched once and reused rather than re-fetched per post: the page stops rendering a
    /// form the moment a user exists, and two of these tests post a <b>second</b> time to
    /// assert that the endpoint refuses. Re-fetching would turn those into a 400 from
    /// antiforgery and stop proving anything about the endpoint's own guard.
    /// </para>
    /// </summary>
    private static async Task<string> SetupTokenAsync(HttpClient client) =>
        await AntiforgeryHelper.TokenFromAsync(client, "/setup");

    private static async Task<HttpResponseMessage> PostSetupAsync(
        HttpClient client,
        string token,
        string email,
        string password = ValidPassword) =>
        await AntiforgeryHelper.PostWithTokenAsync(
            client,
            token,
            "/account/setup",
            ("email", email),
            ("displayName", email),
            ("password", password));

    private static async Task<Guid> AdministratorRoleIdAsync(IdentityDbContext context) =>
        await context.Roles.AsNoTracking()
            .Where(role => role.Name == RoleSeeder.AdministratorRoleName)
            .Select(role => role.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
}
