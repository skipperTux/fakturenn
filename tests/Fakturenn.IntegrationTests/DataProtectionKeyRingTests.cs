using System.Globalization;
using AwesomeAssertions;
using Fakturenn.Infrastructure.DataProtection;
using Fakturenn.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// The promise of <c>PersistKeysToDbContext&lt;DataProtectionDbContext&gt;()</c>: every
/// replica shares one key ring, and the ring outlives the process.
/// <para>
/// Spec section 9 is what depends on this. The authentication cookie, the two-factor
/// cookie, the antiforgery token and — through <c>IdentityDbContext</c>'s value converter
/// — the stored TOTP secret and recovery codes are all protected under this ring. A
/// per-process ring means a forced sign-in on every restart and, with more than one
/// replica, tickets that one instance cannot read from another. Section 12 names this
/// test as the mitigation for that risk, because the symptom is random sign-out rather
/// than an error.
/// </para>
/// </summary>
public sealed class DataProtectionKeyRingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Purpose = "Fakturenn.Tests.KeyRingSurvivesRestart";

    [Fact]
    public async Task A_value_protected_by_one_host_is_readable_by_the_next_one()
    {
        await MigrateAsync();

        // Zero before anything protects anything, so the count below is this test's own
        // doing rather than a row some other fixture left behind.
        (await KeyCountAsync()).Should().Be(0, "the ring starts empty on a clean database");

        const string Secret = "JBSWY3DPEHPK3PXP";
        string protectedValue;

        await using (WebApplication first = BuildHost())
        {
            protectedValue = ProtectorOf(first).Protect(Secret);
        }

        // THE assertion that makes the one below mean what it claims. Without
        // PersistKeysToDbContext, AddDataProtection falls back to a directory under the
        // user's home -- which both hosts share, so the round trip would still succeed and
        // the test would pass while the ring had left the database entirely. Measured: the
        // fallback is silent, and this row count is what notices it.
        (await KeyCountAsync()).Should().BeGreaterThan(
            0, "the ring must be persisted to the database, not to whatever the framework falls back to");

        // A second host against the same database, with the first one disposed: the
        // simulated restart, and equally the second replica.
        await using WebApplication second = BuildHost();

        ProtectorOf(second).Unprotect(protectedValue).Should().Be(
            Secret, "a restart must not invalidate what the instance before it protected");
    }

    // private Methods

    private static IDataProtector ProtectorOf(WebApplication app) =>
        app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);

    /// <summary>
    /// A host carrying the application's own Data Protection registration. The call under
    /// test is <c>AddFakturennIdentity</c> itself — a container assembled here with
    /// <c>AddDataProtection().PersistKeysToDbContext&lt;&gt;()</c> written out again would
    /// prove the framework works and stay green with the production registration deleted.
    /// <para>
    /// Built but never started: nothing here serves a request.
    /// </para>
    /// <para>
    /// Not <c>FakturennWebApplication.Build</c>, and that is measured rather than stylistic.
    /// That method assigns Serilog's <b>static</b> <c>Log.Logger</c>, so a second host built
    /// in this process redirects the log the <see cref="RealHost"/> fixture's in-memory sink
    /// is reading — seven <c>AuthEventLoggingTests</c> failed with "no event was written"
    /// while the endpoints behaved correctly, and a collection attribute did not fix it
    /// because the reassignment outlives the class that caused it.
    /// <c>MigrateEntrypointTests</c> never met this: it builds its hosts in a subprocess.
    /// </para>
    /// </summary>
    private WebApplication BuildHost()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();

        builder.AddFakturennIdentity(postgres.ConnectionString, new DatabaseOptions());

        return builder.Build();
    }

    private async Task MigrateAsync()
    {
        await using DataProtectionDbContext context = new(
            new DbContextOptionsBuilder<DataProtectionDbContext>()
                .UseNpgsql(postgres.ConnectionString)
                .Options);

        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Counted through SQL rather than through the <c>DbContext</c>, so the assertion is
    /// about rows in the schema the deployment backs up, not about an EF round trip that
    /// would read just as happily from a table that had silently moved.
    /// </summary>
    private async Task<long> KeyCountAsync()
    {
        await using NpgsqlConnection connection = new(postgres.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            $"""SELECT COUNT(*) FROM {DataProtectionDbContext.SchemaName}."DataProtectionKeys" """;

        object? count = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        return Convert.ToInt64(count, CultureInfo.InvariantCulture);
    }
}
