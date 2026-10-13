using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Features;
using Fakturenn.Modules.Invoices.Persistence;
using Fakturenn.Modules.Organizations;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.Modules.Organizations.Features;
using Fakturenn.Modules.Organizations.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Fakturenn.IntegrationTests;

public sealed class OrganizationsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    // public Methods
    [Fact]
    public async Task Migrating_creates_exactly_one_empty_organization()
    {
        // Its own database: the class fixture's row is shared with the save test, so its
        // field values there depend on order.
        string connectionString = await postgres.CreateDatabaseAsync("organization_seed");
        await using OrganizationsDbContext db = new(
            new DbContextOptionsBuilder<OrganizationsDbContext>().UseNpgsql(connectionString).Options);
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await OrganizationSeeder.SeedAsync(db, TestContext.Current.CancellationToken);

        List<Organization> rows = await db.Organizations.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);

        rows.Should().ContainSingle().Which.Id.Should().Be(Organization.SingletonId);
        (await new GetOrganization(db).HandleAsync(TestContext.Current.CancellationToken))
            .Should().Be(OrganizationForm.Empty, "every field starts empty or at its default");
    }

    [Fact]
    public async Task The_database_refuses_a_second_organization()
    {
        // Spec section 2: "organization isolation is tested" means exactly this.
        await using OrganizationsDbContext db = await MigratedAsync();
        db.Organizations.Add(new Organization
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "test",
            ModifiedAt = DateTimeOffset.UtcNow,
            ModifiedBy = "test",
        });

        Func<Task> second = () => db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await second.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.ConstraintName.Should().Be("CK_Organizations_Singleton");
    }

    [Fact]
    public async Task A_saved_organization_reads_back_normalized()
    {
        await using OrganizationsDbContext db = await MigratedAsync();
        OrganizationForm form = OrganizationForm.Empty with
        {
            LegalName = "Example Consulting",
            CountryCode = "de",
            VatId = "DE 123456789",
            Iban = "de89 3704 0044 0532 0130 00",
            Currency = "eur",
            NumberPrefix = "R",
            NumberScope = ResetScope.PerDay,
            NumberPadding = 1,
        };

        await using InvoicesDbContext invoices = await MigratedInvoicesAsync();
        IReadOnlyList<OrganizationError> errors =
            await new SaveOrganization(db, new NumberingStatus(invoices)).HandleAsync(form, TestContext.Current.CancellationToken);
        SellerSnapshot seller = await new SellerProvider(db).GetAsync(TestContext.Current.CancellationToken);

        errors.Should().BeEmpty();
        seller.CountryCode.Should().Be("DE");
        seller.VatId.Should().Be("DE123456789");
        seller.Iban.Should().Be("DE89370400440532013000");
        seller.Currency.Should().Be("EUR");
        seller.NumberScheme.Should().Be(new NumberScheme("R", ResetScope.PerDay, 1, 1));
    }

    [Fact]
    public async Task The_number_scheme_locks_after_the_first_allocation()
    {
        await using OrganizationsDbContext db = await MigratedAsync();
        await using InvoicesDbContext invoices = await MigratedInvoicesAsync();

        // Never committed: the status reads on the same connection, so it sees the row, and the
        // rollback keeps the class-shared database unlocked for the save test whatever the order.
        await using IDbContextTransaction transaction =
            await invoices.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await NumberAllocator.AllocateAsync(invoices, "lock-test", 1, TestContext.Current.CancellationToken);

        OrganizationForm current = await new GetOrganization(db).HandleAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<OrganizationError> errors = await new SaveOrganization(db, new NumberingStatus(invoices))
            .HandleAsync(current with { NumberPrefix = current.NumberPrefix + "X" }, TestContext.Current.CancellationToken);

        errors.Should().ContainSingle().Which.Should().Be(OrganizationError.SchemeLocked);
    }

    // private Methods
    private async Task<OrganizationsDbContext> MigratedAsync()
    {
        OrganizationsDbContext db = postgres.Create<OrganizationsDbContext>(options => new(options));
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await OrganizationSeeder.SeedAsync(db, TestContext.Current.CancellationToken);
        return db;
    }

    private async Task<InvoicesDbContext> MigratedInvoicesAsync()
    {
        InvoicesDbContext invoices = postgres.CreateContext();
        await invoices.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return invoices;
    }
}
