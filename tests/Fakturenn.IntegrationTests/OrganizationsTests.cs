using AwesomeAssertions;
using Fakturenn.Modules.Organizations;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.Modules.Organizations.Features;
using Fakturenn.Modules.Organizations.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Fakturenn.IntegrationTests;

public sealed class OrganizationsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    // public Methods
    [Fact]
    public async Task Migrating_creates_exactly_one_empty_organization()
    {
        await using OrganizationsDbContext db = await MigratedAsync();

        List<Organization> rows = await db.Organizations.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);

        // Only the count and the key: the class fixture shares this row with the save test,
        // so its field values depend on order.
        rows.Should().ContainSingle().Which.Id.Should().Be(Organization.SingletonId);
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

        IReadOnlyList<OrganizationError> errors =
            await new SaveOrganization(db).HandleAsync(form, TestContext.Current.CancellationToken);
        SellerSnapshot seller = await new SellerProvider(db).GetAsync(TestContext.Current.CancellationToken);

        errors.Should().BeEmpty();
        seller.CountryCode.Should().Be("DE");
        seller.VatId.Should().Be("DE123456789");
        seller.Iban.Should().Be("DE89370400440532013000");
        seller.Currency.Should().Be("EUR");
        seller.NumberScheme.Should().Be(new NumberScheme("R", ResetScope.PerDay, 1, 1));
    }

    // private Methods
    private async Task<OrganizationsDbContext> MigratedAsync()
    {
        OrganizationsDbContext db = postgres.Create<OrganizationsDbContext>(options => new(options));
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return db;
    }
}
