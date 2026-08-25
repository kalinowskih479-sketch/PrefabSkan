using Microsoft.EntityFrameworkCore;
using SewerScan.Domain.Entities;

namespace SewerScan.Infrastructure.Persistence;

public class SewerScanDbContext : DbContext
{
    public SewerScanDbContext(DbContextOptions<SewerScanDbContext> options)
        : base(options)
    {
    }

    public DbSet<Project> Projects { get; set; } = null!;
    public DbSet<Drawing> Drawings { get; set; } = null!;
    // Add other DbSet<T> for domain entities as needed
}
