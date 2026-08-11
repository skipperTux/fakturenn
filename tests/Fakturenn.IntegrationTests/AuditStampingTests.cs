using AwesomeAssertions;
using Fakturenn.Infrastructure.Persistence;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Fakturenn.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.IntegrationTests;

public sealed class AuditStampingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset _now = new(2026, 8, 10, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_added_row_is_stamped_with_the_acting_user_and_the_clock()
    {
        await using IdentityDbContext context = CreateContext("cr@roeper.biz");
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var role = new Role { Id = Guid.CreateVersion7(), Name = $"audited-{Guid.NewGuid():N}" };
        context.Roles.Add(role);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Role stored = await context.Roles.AsNoTracking()
            .SingleAsync(r => r.Id == role.Id, TestContext.Current.CancellationToken);

        stored.CreatedAt.Should().Be(_now);
        stored.CreatedBy.Should().Be("cr@roeper.biz");
        stored.ModifiedAt.Should().Be(_now);
        stored.ModifiedBy.Should().Be("cr@roeper.biz");
    }

    [Fact]
    public async Task Without_a_signed_in_user_the_row_records_the_system_actor()
    {
        // Migrations, seeding and the operator entrypoints all run without a request.
        await using IdentityDbContext context = CreateContext(userName: null);
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var role = new Role { Id = Guid.CreateVersion7(), Name = $"seeded-{Guid.NewGuid():N}" };
        context.Roles.Add(role);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Role stored = await context.Roles.AsNoTracking()
            .SingleAsync(r => r.Id == role.Id, TestContext.Current.CancellationToken);

        stored.CreatedBy.Should().Be(AuditStamp.SystemUser);
    }

    [Fact]
    public async Task Updating_a_row_moves_ModifiedBy_but_never_CreatedBy()
    {
        await using IdentityDbContext created = CreateContext("first@example.test");
        await created.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var role = new Role { Id = Guid.CreateVersion7(), Name = $"changing-{Guid.NewGuid():N}" };
        created.Roles.Add(role);
        await created.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using IdentityDbContext modified = CreateContext("second@example.test");
        Role tracked = await modified.Roles.SingleAsync(r => r.Id == role.Id, TestContext.Current.CancellationToken);
        tracked.Description = "changed";
        // Creation provenance is a fact about the past; the interceptor must refuse
        // this even when something in the graph tries to overwrite it.
        tracked.CreatedBy = "tampered";
        await modified.SaveChangesAsync(TestContext.Current.CancellationToken);

        Role stored = await modified.Roles.AsNoTracking()
            .SingleAsync(r => r.Id == role.Id, TestContext.Current.CancellationToken);

        stored.CreatedBy.Should().Be("first@example.test");
        stored.ModifiedBy.Should().Be("second@example.test");
    }

    private IdentityDbContext CreateContext(string? userName)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(
                new StubClock(_now), new StubCurrentUser(userName)))
            .Options;

        return new IdentityDbContext(options);
    }

    private sealed class StubClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class StubCurrentUser(string? userName) : ICurrentUserAccessor
    {
        public string? UserName => userName;
    }
}
