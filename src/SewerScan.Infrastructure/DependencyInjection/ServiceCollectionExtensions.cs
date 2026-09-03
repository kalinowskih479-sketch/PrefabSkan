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
        // Use Windows-capable extractor when running UI (supports OCR fallback)
        // Register the WindowsPdfTextExtractor from UI assembly if available; otherwise fall back to PdfTextExtractor
        try
        {
            // attempt to resolve type by name to avoid hard dependency from Infrastructure to UI
            var t = Type.GetType("SewerScan.UI.Pdf.WindowsPdfTextExtractor, SewerScan.UI");
            if (t != null && typeof(SewerScan.Application.Interfaces.ITextExtractor).IsAssignableFrom(t))
            {
                services.AddSingleton(typeof(SewerScan.Application.Interfaces.ITextExtractor), t);
            }
            else
            {
                services.AddSingleton<ITextExtractor, PdfTextExtractor>();
            }
        }
        catch
        {
            services.AddSingleton<ITextExtractor, PdfTextExtractor>();
        }
        services.AddSingleton<IProjectParser, SewerProjectParser>();
        // Application service - PdfAnalyzer is in Application assembly
        services.AddSingleton<IPdfAnalyzer, PdfAnalyzer>();

        return services;
    }
}
