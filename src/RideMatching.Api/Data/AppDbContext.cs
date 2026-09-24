using Microsoft.EntityFrameworkCore;
using RideMatching.Api.Domain.Entities;

namespace RideMatching.Api.Data;

/// <summary>
/// EF Core context. SQL Server is the authoritative store for rides, drivers,
/// and the assignment audit trail.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Ride> Rides => Set<Ride>();
    public DbSet<RideAssignment> RideAssignments => Set<RideAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Driver>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Name).IsRequired().HasMaxLength(200);
            e.Property(d => d.Status).HasConversion<int>().IsRequired();
            e.Property(d => d.CreatedAt).IsRequired();

            e.HasIndex(d => d.Status);
            e.HasIndex(d => d.LastSeenAt);
        });

        modelBuilder.Entity<Ride>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.RiderId).IsRequired();
            e.Property(r => r.Status).HasConversion<int>().IsRequired();
            e.Property(r => r.CreatedAt).IsRequired();
            e.Property(r => r.IdempotencyKey).HasMaxLength(200);

            e.HasIndex(r => r.Status);
            e.HasIndex(r => r.DriverId);
            e.HasIndex(r => r.CreatedAt);

            // Idempotency is scoped to the rider: the same key maps to at most one
            // ride per rider, while different riders may reuse the same key value.
            e.HasIndex(r => new { r.RiderId, r.IdempotencyKey })
                .IsUnique()
                .HasFilter("[IdempotencyKey] IS NOT NULL");

            e.HasOne(r => r.Driver)
                .WithMany()
                .HasForeignKey(r => r.DriverId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RideAssignment>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.AttemptNumber).IsRequired();
            e.Property(a => a.AssignedAt).IsRequired();
            e.Property(a => a.Success).IsRequired();

            e.HasIndex(a => a.RideId);
            e.HasIndex(a => a.DriverId);

            e.HasOne(a => a.Ride)
                .WithMany(r => r.Assignments)
                .HasForeignKey(a => a.RideId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(a => a.Driver)
                .WithMany()
                .HasForeignKey(a => a.DriverId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
