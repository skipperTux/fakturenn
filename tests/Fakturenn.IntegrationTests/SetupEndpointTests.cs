using System.Net;
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
public sealed class SetupEndpointTests(SetupHostFixture host)
{
    private const string ValidPassword = "Korrekt-Pferd-42";

    [Fact]
    public async Task Posting_to_an_empty_instance_creates_one_administrator_who_must_enrol_totp()
    {
        await host.ResetUsersAsync(TestContext.Current.CancellationToken);

        using HttpClient client = host.CreateClient();
        using HttpResponseMessage response = await PostSetupAsync(client, "first@example.test");

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

        using HttpClient client = host.CreateClient();
        using (HttpResponseMessage first = await PostSetupAsync(client, "owner@example.test"))
        {
            first.StatusCode.Should().Be(HttpStatusCode.Found);
        }

        using HttpResponseMessage second = await PostSetupAsync(client, "intruder@example.test");

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

        using HttpClient client = host.CreateClient();
        using Barrier startTogether = new(Racers);

        Task<HttpResponseMessage>[] posts = [.. Enumerable.Range(0, Racers).Select(index =>
            Task.Run(
                async () =>
                {
                    // Released as one, so the count checks overlap rather than queueing.
                    startTogether.SignalAndWait(TestContext.Current.CancellationToken);

                    return await PostSetupAsync(client, $"racer-{index}@example.test");
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

        using HttpClient client = host.CreateClient();
        using HttpResponseMessage response =
            await PostSetupAsync(client, "weak@example.test", password: "short");

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

        using HttpClient client = host.CreateClient();
        using HttpResponseMessage response = await PostSetupAsync(client, "bypass@example.test");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using IdentityDbContext context = host.CreateIdentityContext();

        List<string?> emails = await context.Users.AsNoTracking()
            .Select(user => user.Email)
            .ToListAsync(TestContext.Current.CancellationToken);
        emails.Should().ContainSingle().Which.Should().Be("planted@example.test");
    }

    private static async Task<HttpResponseMessage> PostSetupAsync(
        HttpClient client,
        string email,
        string password = ValidPassword)
    {
        // No antiforgery token: this measures what the pipeline actually enforces for a
        // hand-rolled form post rather than assuming it.
        using FormUrlEncodedContent form = new(
        [
            new KeyValuePair<string, string>("email", email),
            new KeyValuePair<string, string>("displayName", email),
            new KeyValuePair<string, string>("password", password),
        ]);

        return await client.PostAsync(new Uri("/account/setup", UriKind.Relative), form, TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> AdministratorRoleIdAsync(IdentityDbContext context) =>
        await context.Roles.AsNoTracking()
            .Where(role => role.Name == RoleSeeder.AdministratorRoleName)
            .Select(role => role.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
}
