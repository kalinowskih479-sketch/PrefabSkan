using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SewerScan.Infrastructure.Persistence;
using SewerScan.Application.Interfaces;
using SewerScan.Infrastructure.Pdf;
using SewerScan.Infrastructure.Parsers;
using SewerScan.Application.Services;

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
        // PDF analysis
        services.AddSingleton<ITextExtractor, PdfTextExtractor>();
        services.AddSingleton<IProjectParser, SimpleProjectParser>();
        // Application service - PdfAnalyzer is in Application assembly
        services.AddSingleton<IPdfAnalyzer, PdfAnalyzer>();

        return services;
    }
}
