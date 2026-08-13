using AwesomeAssertions;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Fakturenn.IntegrationTests;

public sealed class IdentityTokenEncryptionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string SecretValue = "2W2NZBPUT2YX3LP3SUMMXICIO2INDYYU";

    [Fact]
    public async Task A_token_value_is_not_readable_as_plaintext_in_the_column()
    {
        // This is the only test that would notice the value converter being dropped
        // in a future refactor: a round-trip through EF alone would still pass.
        await using IdentityDbContext context = CreateContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = "probe@example.test",
            Email = "probe@example.test",
            DisplayName = "Probe",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        context.Users.Add(user);
        context.Set<IdentityUserToken<Guid>>().Add(new IdentityUserToken<Guid>
        {
            UserId = user.Id,
            LoginProvider = "[AspNetUserStore]",
            Name = "AuthenticatorKey",
            Value = SecretValue,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """SELECT "Value" FROM identity."AspNetUserTokens" WHERE "Name" = 'AuthenticatorKey'""";
        object? stored = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        stored.Should().NotBeNull();
        stored!.ToString().Should().NotBe(SecretValue, "the shared secret must not be stored in plaintext");
        stored.ToString().Should().NotContain(SecretValue);
    }

    [Fact]
    public async Task A_token_value_round_trips_through_the_converter()
    {
        await using IdentityDbContext write = CreateContext();
        await write.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var userId = Guid.CreateVersion7();
        write.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = $"roundtrip-{userId:N}@example.test",
            Email = $"roundtrip-{userId:N}@example.test",
            DisplayName = "Round Trip",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        write.Set<IdentityUserToken<Guid>>().Add(new IdentityUserToken<Guid>
        {
            UserId = userId,
            LoginProvider = "[AspNetUserStore]",
            Name = "RecoveryCodes",
            Value = "AAAAA-11111;BBBBB-22222",
        });
        await write.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using IdentityDbContext read = CreateContext();
        IdentityUserToken<Guid> token = await read.Set<IdentityUserToken<Guid>>()
            .AsNoTracking()
            .SingleAsync(t => t.UserId == userId, TestContext.Current.CancellationToken);

        token.Value.Should().Be("AAAAA-11111;BBBBB-22222");
    }

    private IdentityDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(postgres.ConnectionString)
                .Options,
            DataProtectionProvider.Create("Fakturenn.Tests"));
}
