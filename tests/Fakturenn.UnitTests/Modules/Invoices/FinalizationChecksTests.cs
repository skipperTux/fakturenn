using AwesomeAssertions;
using Fakturenn.Modules.Catalog.Contracts;
using Fakturenn.Modules.Customers.Contracts;
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.Modules.Projects.Contracts;

namespace Fakturenn.UnitTests.Modules.Invoices;

public sealed class FinalizationChecksTests
{
    // private static readonly Fields
    private static readonly Guid _customerId = Guid.CreateVersion7();
    private static readonly SellerSnapshot _seller = new(
        "Example Consulting", "DE", "DE123456789", "DE89370400440532013000", "EUR", "Europe/Berlin", 14,
        IssueDateDefault.Today, new NumberScheme("R", ResetScope.PerDay, 1, 1));
    private static readonly CustomerSnapshot _customer = new(_customerId, "Example Client GmbH", "DE", null, "C-4711");
    private static readonly CatalogItemSnapshot _item = new(Guid.CreateVersion7(), "DEV-BACKEND", "Backend development", "HUR", 100m, "EUR", 19m);

    // public Methods
    [Fact]
    public void The_walking_skeleton_passes()
    {
        FinalizationChecks.Check(_seller, _customer, null, _customerId, [_item]).Should().BeNull();
    }

    [Theory]
    [InlineData("LegalName")]
    [InlineData("CountryCode")]
    [InlineData("VatId")]
    [InlineData("Iban")]
    [InlineData("Currency")]
    public void Each_missing_seller_field_refuses(string field)
    {
        SellerSnapshot seller = field switch
        {
            "LegalName" => _seller with { LegalName = "" },
            "CountryCode" => _seller with { CountryCode = "" },
            "VatId" => _seller with { VatId = "" },
            "Iban" => _seller with { Iban = "" },
            "Currency" => _seller with { Currency = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        FinalizationChecks.Check(seller, _customer, null, _customerId, [_item]).Should().Be(FinalizationRefusal.SellerIncomplete);
    }

    [Fact]
    public void A_missing_customer_refuses()
    {
        FinalizationChecks.Check(_seller, null, null, _customerId, [_item]).Should().Be(FinalizationRefusal.CustomerMissing);
    }

    [Fact]
    public void Another_customers_project_refuses()
    {
        ProjectSnapshot project = new(Guid.CreateVersion7(), Guid.CreateVersion7(), "PRJ-2026-083");

        FinalizationChecks.Check(_seller, _customer, project, _customerId, [_item])
            .Should().Be(FinalizationRefusal.ProjectBelongsToAnotherCustomer);
    }

    [Fact]
    public void A_deleted_catalog_item_refuses()
    {
        FinalizationChecks.Check(_seller, _customer, null, _customerId, [null]).Should().Be(FinalizationRefusal.CatalogItemMissing);
    }
}
