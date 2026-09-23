using Microsoft.EntityFrameworkCore;
using RideMatching.Api.Domain.Entities;
using RideMatching.Api.Domain.Enums;

namespace RideMatching.Api.Data;

/// <summary>
/// Development-only seeding. Ensures the schema exists and inserts three demo
/// drivers positioned to make the matching demonstration easy.
/// </summary>
public static class DbSeeder
{
    public static readonly Guid Driver1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Driver2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Driver3Id = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Drivers.AnyAsync(ct))
        {
            return;
        }

        db.Drivers.AddRange(
            new Driver
            {
                Id = Driver1Id,
                Name = "Driver 1",
                Status = DriverStatus.Offline,
                Latitude = 28.6139,
                Longitude = 77.2090,
                CreatedAt = DateTime.UtcNow
            },
            new Driver
            {
                Id = Driver2Id,
                Name = "Driver 2",
                Status = DriverStatus.Offline,
                Latitude = 28.6200,
                Longitude = 77.2150,
                CreatedAt = DateTime.UtcNow
            },
            new Driver
            {
                Id = Driver3Id,
                Name = "Driver 3",
                Status = DriverStatus.Offline,
                Latitude = 28.7000,
                Longitude = 77.3000,
                CreatedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync(ct);
    }
}
