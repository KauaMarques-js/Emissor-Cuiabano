using FiscalPlatform.Application;
using FiscalPlatform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FiscalPlatform.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CertificateMetadata> Certificates => Set<CertificateMetadata>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<FiscalDocument> Documents => Set<FiscalDocument>();
    public DbSet<FiscalDocumentItem> DocumentItems => Set<FiscalDocumentItem>();
    public DbSet<FiscalEvent> Events => Set<FiscalEvent>();
    public DbSet<TransmissionAttempt> Attempts => Set<TransmissionAttempt>();
    public DbSet<EmissionBatch> Batches => Set<EmissionBatch>();
    public DbSet<BatchItem> BatchItems => Set<BatchItem>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.Email).IsUnique();

        b.Entity<Company>(e =>
        {
            e.OwnsOne(c => c.Address);
            e.Navigation(c => c.Address).IsRequired();
            e.HasIndex(c => c.Cnpj).IsUnique();
        });
        b.Entity<CertificateMetadata>().HasIndex(c => new { c.CompanyId, c.IsActive });

        b.Entity<Customer>(e =>
        {
            e.OwnsOne(c => c.Address);
            e.Navigation(c => c.Address).IsRequired();
            e.HasIndex(c => new { c.CompanyId, c.Document }).IsUnique();
        });
        b.Entity<Product>().HasIndex(p => new { p.CompanyId, p.Code }).IsUnique();
        b.Entity<Service>().HasIndex(s => new { s.CompanyId, s.Code }).IsUnique();

        b.Entity<FiscalDocument>(e =>
        {
            e.OwnsOne(d => d.Nfe);
            e.OwnsOne(d => d.Nfse);
            e.Navigation(d => d.Nfe).IsRequired();
            e.Navigation(d => d.Nfse).IsRequired();
            // Impede que duas operações transmitam o mesmo documento ao mesmo tempo.
            e.Property(d => d.Status).IsConcurrencyToken();
            e.HasOne(d => d.Company).WithMany().HasForeignKey(d => d.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(d => d.Customer).WithMany().HasForeignKey(d => d.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(d => d.Items).WithOne().HasForeignKey(i => i.FiscalDocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(d => new { d.CompanyId, d.Type, d.Status });
            e.HasIndex(d => d.IdempotencyKey).IsUnique();
        });

        b.Entity<FiscalEvent>().HasIndex(e => e.FiscalDocumentId);
        b.Entity<TransmissionAttempt>(e =>
        {
            e.HasOne(a => a.FiscalDocument).WithMany().HasForeignKey(a => a.FiscalDocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => new { a.CompanyId, a.StartedAt });
        });
        b.Entity<EmissionBatch>().HasMany(x => x.Items).WithOne().HasForeignKey(i => i.BatchId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<BatchItem>().HasOne(i => i.FiscalDocument).WithMany().HasForeignKey(i => i.FiscalDocumentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AuditLog>().HasIndex(a => new { a.CompanyId, a.CreatedAt });
    }
}

/// <summary>Usado apenas pelo `dotnet ef` para gerar migrações.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=design.db").Options);
}
