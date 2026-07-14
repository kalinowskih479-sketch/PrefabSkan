using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SewerScan.Infrastructure.Persistence;

namespace SewerScan.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure EF Core with SQLite
        var conn = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=sewerscan.db";
        services.AddDbContext<SewerScanDbContext>(options =>
            options.UseSqlite(conn));

        // Register other infrastructure services (repositories, OCR, AI, logging adapters etc.)

        return services;
    }
}
