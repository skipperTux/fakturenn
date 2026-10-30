using AwesomeAssertions;
using Fakturenn.SharedKernel;
using Fakturenn.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Fakturenn.UnitTests.SharedKernel;

public sealed class AuditColumnConfigurationTests
{
    [Fact]
    public void An_auditable_entity_gets_required_audit_columns_with_bounded_user_names()
    {
        ModelBuilder modelBuilder = new();
        modelBuilder.Entity<AuditedThing>();

        modelBuilder.ConfigureAuditColumns();

        IMutableEntityType entityType = modelBuilder.Model.FindEntityType(typeof(AuditedThing))!;
        entityType.FindProperty(nameof(IAuditable.CreatedBy))!.GetMaxLength().Should().Be(256);
        entityType.FindProperty(nameof(IAuditable.ModifiedBy))!.GetMaxLength().Should().Be(256);
        foreach (string name in new[] { nameof(IAuditable.CreatedBy), nameof(IAuditable.ModifiedBy), nameof(IAuditable.CreatedAt), nameof(IAuditable.ModifiedAt) })
        {
            entityType.FindProperty(name)!.IsNullable.Should().BeFalse(name);
        }
    }

    [Fact]
    public void An_entity_that_is_not_auditable_is_left_alone()
    {
        ModelBuilder modelBuilder = new();
        modelBuilder.Entity<PlainThing>().Property(thing => thing.Name);

        modelBuilder.ConfigureAuditColumns();

        modelBuilder.Model.FindEntityType(typeof(PlainThing))!
            .FindProperty(nameof(PlainThing.Name))!.GetMaxLength().Should().BeNull();
    }

    private sealed class AuditedThing : IAuditable
    {
        public int Id { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public string CreatedBy { get; set; } = "";

        public DateTimeOffset ModifiedAt { get; set; }

        public string ModifiedBy { get; set; } = "";
    }

    private sealed class PlainThing
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }
}
