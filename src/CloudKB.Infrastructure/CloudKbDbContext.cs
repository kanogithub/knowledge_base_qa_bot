using Microsoft.EntityFrameworkCore;

namespace CloudKB.Infrastructure;

public class CloudKbDbContext : DbContext
{
    public CloudKbDbContext(DbContextOptions<CloudKbDbContext> options) : base(options)
    {
    }

    public DbSet<TenantSection> TenantSections => Set<TenantSection>();
    public DbSet<IndexCompilationJob> IndexCompilationJobs => Set<IndexCompilationJob>();
    public DbSet<TenantFile> TenantFiles => Set<TenantFile>();
    public DbSet<TenantFileState> TenantFileStates => Set<TenantFileState>();
    public DbSet<IndexAuditLog> IndexAuditLogs => Set<IndexAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── TenantSection ──
        modelBuilder.Entity<TenantSection>(e =>
        {
            e.ToTable("tenant_sections");
            e.HasKey(x => x.Id);
            
            e.Property(x => x.HeadingPath)
                .HasColumnType("jsonb");
                
            e.Property(x => x.Tokens)
                .HasColumnType("jsonb");

            e.HasIndex(x => x.TenantId)
                .HasDatabaseName("ix_tenant_sections_tenant_id");

            e.HasIndex(x => new { x.TenantId, x.FileName })
                .HasDatabaseName("ix_tenant_sections_tenant_file");
        });

        // ── IndexCompilationJob ──
        modelBuilder.Entity<IndexCompilationJob>(e =>
        {
            e.ToTable("index_compilation_jobs");
            e.HasKey(x => x.TaskId);
            
            e.Property(x => x.FileNames)
                .HasColumnType("jsonb");

            e.HasIndex(x => x.TenantId)
                .HasDatabaseName("ix_compilation_jobs_tenant_id");

            e.HasIndex(x => new { x.TenantId, x.Status })
                .HasDatabaseName("ix_compilation_jobs_status");

            e.HasIndex(x => x.RequestedAt)
                .HasDatabaseName("ix_compilation_jobs_requested_at");
        });

        // ── TenantFile ──
        modelBuilder.Entity<TenantFile>(e =>
        {
            e.ToTable("tenant_files");
            e.HasKey(x => x.Id);

            e.HasIndex(x => new { x.TenantId, x.FileName })
                .IsUnique()
                .HasDatabaseName("uq_tenant_files_tenant_filename");

            e.HasIndex(x => x.TenantId)
                .HasDatabaseName("ix_tenant_files_tenant_id");
        });

        // ── TenantFileState ──
        modelBuilder.Entity<TenantFileState>(e =>
        {
            e.ToTable("tenant_file_states");
            e.HasKey(x => x.Id);

            e.HasIndex(x => x.TenantId)
                .HasDatabaseName("ix_tenant_file_states_tenant_id");
        });

        // ── IndexAuditLog ──
        modelBuilder.Entity<IndexAuditLog>(e =>
        {
            e.ToTable("index_audit_logs");
            e.HasKey(x => x.Id);

            e.HasIndex(x => new { x.TenantId, x.LoggedAt })
                .HasDatabaseName("ix_index_audit_logs_tenant_logged");
        });
    }
}
