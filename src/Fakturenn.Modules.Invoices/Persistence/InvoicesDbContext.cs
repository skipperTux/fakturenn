using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Invoices.Persistence;

/// <summary>
/// The Invoices module owns this context and its migrations. No other module
/// may reference the entities it maps.
/// </summary>
public sealed class InvoicesDbContext(DbContextOptions<InvoicesDbContext> options)
    : DbContext(options)
{
    // public const Fields
    public const string SchemaName = "invoices";

    // public Properties
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<NumberCounter> NumberCounters => Set<NumberCounter>();
    public DbSet<InvoiceSnapshotRecord> Snapshots => Set<InvoiceSnapshotRecord>();

    // internal Methods
    /// <summary>
    /// Finalization and draft saves take this row lock before loading, so each sees the invoice
    /// only after the other has committed (spec section 5 step 1, review I1).
    /// </summary>
    internal Task LockInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        Database.ExecuteSqlAsync($"SELECT 1 FROM invoices.\"Invoices\" WHERE \"Id\" = {invoiceId} FOR UPDATE", cancellationToken);

    // protected Methods
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<Invoice>(invoice =>
        {
            invoice.Property(i => i.State).HasConversion<string>().HasMaxLength(16);
            invoice.Property(i => i.Number).HasMaxLength(40);
            invoice.HasIndex(i => i.Number).IsUnique().HasFilter("\"Number\" IS NOT NULL");
            invoice.Property(i => i.Currency).HasMaxLength(3);
            invoice.Property(i => i.NetAmount).HasPrecision(18, 2);
            invoice.Property(i => i.VatAmount).HasPrecision(18, 2);
            invoice.Property(i => i.GrossAmount).HasPrecision(18, 2);
            invoice.Property(i => i.TaxCategory).HasConversion<string>().HasMaxLength(4);
            invoice.Property(i => i.VatRate).HasPrecision(5, 2);
            invoice.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            invoice.Navigation(i => i.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<InvoiceLine>(line =>
        {
            // The aggregate assigns the id. A generated key that is already set makes EF treat a
            // line added to a loaded invoice as existing, and SaveChanges updates zero rows.
            line.Property(l => l.Id).ValueGeneratedNever();
            line.Property(l => l.Quantity).HasConversion(q => q.Value, v => new Quantity(v)).HasPrecision(18, 4);
            line.Property(l => l.NetAmount).HasPrecision(18, 2);
            line.Property(l => l.TaxCategory).HasConversion<string>().HasMaxLength(4);
            line.Property(l => l.VatRate).HasPrecision(5, 2);
        });

        modelBuilder.Entity<NumberCounter>(counter =>
        {
            counter.HasKey(c => c.ScopeKey);
            counter.Property(c => c.ScopeKey).HasMaxLength(16);
        });

        modelBuilder.Entity<InvoiceSnapshotRecord>(snapshot =>
        {
            snapshot.HasKey(s => s.InvoiceId);
            snapshot.HasOne<Invoice>().WithOne().HasForeignKey<InvoiceSnapshotRecord>(s => s.InvoiceId);
        });

        modelBuilder.ConfigureAuditColumns();
        base.OnModelCreating(modelBuilder);
    }
}
