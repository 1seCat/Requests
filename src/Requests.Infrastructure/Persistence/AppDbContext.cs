using Microsoft.EntityFrameworkCore;
using Requests.Domain.Entities;
using Requests.Infrastructure.Persistence.Entities;

namespace Requests.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    public DbSet<Request> Requests => Set<Request>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Request>(e =>
        {
            e.ToTable("Requests", t =>
            {
                t.HasCheckConstraint("CK_Requests_Status",
                    "[Status] IN ('New','InProgress','Completed','Rejected','Cancelled')");
                t.HasCheckConstraint("CK_Requests_Title_NotBlank",
                    "LEN(LTRIM(RTRIM([Title]))) > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.ExternalId).HasMaxLength(100).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Status).HasMaxLength(20).HasConversion<string>().IsRequired();
            e.Property(x => x.CreatedAt).HasColumnType("datetime2");
            e.Property(x => x.UpdatedAt).HasColumnType("datetime2");
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

            e.HasIndex(x => x.ExternalId).IsUnique().HasDatabaseName("UX_Requests_ExternalId");
            e.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("IX_Requests_Status_CreatedAt");
            e.HasIndex(x => x.CreatedAt).HasDatabaseName("IX_Requests_CreatedAt");
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("OutboxMessages");
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            e.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnType("datetime2");
            e.Property(x => x.PublishedAt).HasColumnType("datetime2");
            e.Property(x => x.ClaimedAt).HasColumnType("datetime2");
            e.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("IX_Outbox_Status_CreatedAt");
        });

        b.Entity<ProcessedMessage>(e =>
        {
            e.ToTable("ProcessedMessages");
            e.HasKey(x => x.EventId);
            e.Property(x => x.ProcessedAt).HasColumnType("datetime2");
        });
    }
}