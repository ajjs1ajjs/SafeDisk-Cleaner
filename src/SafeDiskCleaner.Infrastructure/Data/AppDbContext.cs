using Microsoft.EntityFrameworkCore;

namespace SafeDiskCleaner.Infrastructure.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();
    public DbSet<QuarantineEntity> Quarantines => Set<QuarantineEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLogEntry>(entity =>
        {
            entity.ToTable("audit_log");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Path).HasMaxLength(1024);
            entity.Property(e => e.Detail).HasMaxLength(2048);
            entity.HasIndex(e => e.Timestamp);
        });

        modelBuilder.Entity<QuarantineEntity>(entity =>
        {
            entity.ToTable("quarantine");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OriginalPath).HasMaxLength(1024);
            entity.Property(e => e.StoredName).HasMaxLength(512);
            entity.HasIndex(e => e.QuarantinedAt);
        });
    }
}
