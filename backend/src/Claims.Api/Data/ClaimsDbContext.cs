using Claims.Api.Domain;
using Claims.Api.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Data;

public class ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : DbContext(options)
{
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimEvent> ClaimEvents => Set<ClaimEvent>();
    public DbSet<InformationRequest> InformationRequests => Set<InformationRequest>();
    public DbSet<Market> Markets => Set<Market>();
    public DbSet<MarketSequence> MarketSequences => Set<MarketSequence>();
    public DbSet<User> Users => Set<User>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        // Money: numeric(19,4) gives room for large KRW amounts and any ISO currency precision (DR-3).
        b.Properties<decimal>().HavePrecision(19, 4);
        // Enums stored as readable strings, not magic integers.
        b.Properties<ClaimStatus>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<ClaimType>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<ClaimEventType>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<UserRole>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Market>(e =>
        {
            e.HasKey(m => m.Code);
            e.Property(m => m.Code).HasMaxLength(2);
            e.Property(m => m.CurrencyCode).HasMaxLength(3);
        });

        b.Entity<MarketSequence>(e =>
        {
            e.HasKey(s => s.MarketCode);
            e.HasOne<Market>().WithOne().HasForeignKey<MarketSequence>(s => s.MarketCode);
        });

        b.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).HasMaxLength(64);
        });

        b.Entity<Claim>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).ValueGeneratedNever();
            e.HasIndex(c => c.Reference).IsUnique();                   // DR-4
            e.HasIndex(c => new { c.Status, c.SubmittedAt });           // H-1 queue
            e.HasIndex(c => new { c.AssignedHandlerId, c.Status });     // H-3 workload
            e.HasIndex(c => c.ClaimantId);                              // C-3

            e.Property(c => c.Reference).HasMaxLength(20);
            e.Property(c => c.MarketCode).HasMaxLength(2);
            e.Property(c => c.CurrencyCode).HasMaxLength(3);
            e.Property(c => c.PolicyNumber).HasMaxLength(40);
            e.Property(c => c.IncidentLocation).HasMaxLength(200);
            e.Property(c => c.Description).HasMaxLength(4000);
            e.Property(c => c.VehicleRegistration).HasMaxLength(20);
            e.Property(c => c.PropertyAddress).HasMaxLength(300);
            e.Property(c => c.RejectionReason).HasMaxLength(1000);

            e.HasOne<Market>().WithMany().HasForeignKey(c => c.MarketCode);
            e.HasOne<User>().WithMany().HasForeignKey(c => c.ClaimantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(c => c.AssignedHandlerId).OnDelete(DeleteBehavior.Restrict);

            // DR-7: Postgres xmin system column as the optimistic concurrency token.
            e.Property(c => c.Version).IsRowVersion();

            e.HasMany(c => c.Events).WithOne().HasForeignKey(ev => ev.ClaimId);
            e.HasMany(c => c.InformationRequests).WithOne().HasForeignKey(r => r.ClaimId);
            e.Navigation(c => c.Events).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Navigation(c => c.InformationRequests).UsePropertyAccessMode(PropertyAccessMode.Field);

            e.Ignore(c => c.UncommittedEvents);
            e.Ignore(c => c.IsOpen);
            e.Ignore(c => c.OpenInformationRequest);
        });

        b.Entity<ClaimEvent>(e =>
        {
            e.HasKey(ev => ev.Id);
            e.Property(ev => ev.Id).ValueGeneratedNever(); // ids are assigned in the domain, so new children are inserted, not updated
            e.HasIndex(ev => new { ev.ClaimId, ev.Sequence }).IsUnique(); // gap-free, no duplicates
            e.Property(ev => ev.ActorId).HasMaxLength(64);
            e.Property(ev => ev.Note).HasMaxLength(4000);
        });

        b.Entity<InformationRequest>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).ValueGeneratedNever();
            e.Property(r => r.Question).HasMaxLength(2000);
            e.Property(r => r.Response).HasMaxLength(4000);
            e.Ignore(r => r.IsOpen);
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("Outbox");
            e.HasKey(o => o.Id);
            e.Property(o => o.Id).UseIdentityAlwaysColumn();
            e.HasIndex(o => o.EventId).IsUnique();
            // Partial index: the publisher only ever scans unpublished rows.
            e.HasIndex(o => o.Id).HasFilter("\"PublishedAt\" IS NULL").HasDatabaseName("IX_Outbox_Unpublished");
            e.Property(o => o.Payload).HasColumnType("jsonb");
        });
    }
}
