using AwesomeAssertions;
using Fakturenn.SharedKernel;

namespace Fakturenn.UnitTests.SharedKernel;

public sealed class AuditStampTests
{
    private static readonly DateTimeOffset _now =
        new(2026, 8, 10, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_entity_with_no_provenance_is_stamped_with_the_current_user_and_time()
    {
        (DateTimeOffset createdAt, string createdBy) =
            AuditStamp.ForAdded(default, null, _now, "cr@roeper.biz");

        createdAt.Should().Be(_now);
        createdBy.Should().Be("cr@roeper.biz");
    }

    [Fact]
    public void A_new_entity_that_already_carries_provenance_keeps_it()
    {
        // A seeder or an import knows the real provenance. Overwriting it would
        // replace a fact with the identity of whoever happened to run the import.
        var imported = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        (DateTimeOffset createdAt, string createdBy) =
            AuditStamp.ForAdded(imported, "legacy-import", _now, "cr@roeper.biz");

        createdAt.Should().Be(imported);
        createdBy.Should().Be("legacy-import");
    }

    [Fact]
    public void A_blank_creator_counts_as_absent()
    {
        (_, string createdBy) = AuditStamp.ForAdded(default, "   ", _now, "cr@roeper.biz");

        createdBy.Should().Be("cr@roeper.biz");
    }

    [Fact]
    public void No_signed_in_user_resolves_to_the_system_actor()
    {
        // Migrations, seeding and the operator entrypoints all run without a
        // request, and they must still produce a truthful actor rather than an
        // empty string.
        AuditStamp.ResolveUser(null).Should().Be(AuditStamp.SystemUser);
        AuditStamp.ResolveUser("  ").Should().Be(AuditStamp.SystemUser);
    }

    [Fact]
    public void A_signed_in_user_resolves_to_their_name()
    {
        AuditStamp.ResolveUser("cr@roeper.biz").Should().Be("cr@roeper.biz");
    }
}
