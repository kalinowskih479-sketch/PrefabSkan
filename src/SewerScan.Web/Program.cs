using Microsoft.AspNetCore.Http.Features;
using SewerScan.Application.DTO;
using SewerScan.Application.Interfaces;
using SewerScan.Application.Services;
using SewerScan.Infrastructure.Excel;
using SewerScan.Infrastructure.Parsers;
using SewerScan.Infrastructure.Pdf;
using SewerScan.Web;

// In a published app the working directory can differ from the executable
// directory (for example when Windows starts it from a shortcut). Always
// resolve the actual folder containing wwwroot, both in dev and publish.
var contentRoot = new[]
{
    AppContext.BaseDirectory,
    Directory.GetCurrentDirectory(),
    Path.Combine(Directory.GetCurrentDirectory(), "src", "SewerScan.Web")
}.FirstOrDefault(root => File.Exists(Path.Combine(root, "wwwroot", "index.html")))
    ?? Directory.GetCurrentDirectory();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRoot
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 90L * 1024 * 1024;
});
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 85L * 1024 * 1024;
});
builder.Services.AddSingleton<ITextExtractor, PdfTextExtractor>();
builder.Services.AddSingleton<IProjectParser, SewerProjectParser>();
builder.Services.AddSingleton<IPdfAnalyzer, PdfAnalyzer>();
builder.Services.AddSingleton<AnalysisStore>();
builder.Services.AddSingleton<AnalysisUploadService>();
builder.Services.AddSingleton(new SemaphoreSlim(1, 1));

var app = builder.Build();

// The preview is local-only and intentionally has no authentication. Never bind
// it to a public interface without adding authentication and transport security.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self'; " +
        "object-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api"))
        context.Response.Headers["Cache-Control"] = "no-store";

    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    product = "PrefabScan Web",
    mode = "local-preview"
}));

app.MapPost("/api/analyze", async Task<IResult> (
    HttpRequest request,
    IPdfAnalyzer analyzer,
    AnalysisUploadService uploads,
    AnalysisStore store,
    SemaphoreSlim limiter) =>
{
    // A custom header makes a cross-origin HTML form insufficient to start a
    // costly OCR job on the user's localhost. CORS is deliberately not enabled.
    if (request.Headers["X-PrefabScan-Client"] != "web")
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    if (request.ContentLength > 90L * 1024 * 1024)
        return Results.Problem("Przekroczono limit wielkości żądania.", statusCode: 413);

    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Prześlij dokumenty jako formularz multipart." });

    if (!await limiter.WaitAsync(0, request.HttpContext.RequestAborted))
        return Results.Json(new { error = "Trwa analiza innego zestawu. Spróbuj ponownie." }, statusCode: 429);

    try
    {
        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
        }
        catch (InvalidDataException)
        {
            return Results.Json(new { error = "Niepoprawny formularz lub przekroczony limit danych." }, statusCode: 413);
        }

        var validation = uploads.Validate(form.Files);
        if (validation != null)
            return Results.BadRequest(new { error = validation });

        var outcome = await uploads.AnalyzeAsync(form.Files, analyzer, request.HttpContext.RequestAborted);
        if (outcome.Project == null)
            return Results.UnprocessableEntity(new
            {
                error = "Nie udało się odczytać żadnego pliku PDF.",
                warnings = outcome.Warnings
            });

        var project = outcome.Project;
        var id = store.Save(project);
        return Results.Ok(new
        {
            analysisId = id,
            drawingType = project.DrawingType,
            documents = project.SourceDocuments,
            counts = new
            {
                manholes = project.Manholes.Count,
                inlets = project.Inlets.Count,
                pipes = project.Pipes.Count,
                incomplete = project.Manholes.Count(m => m.CompletenessPercent < 70)
            },
            manholes = project.Manholes.Select(m => new
            {
                m.Identifier, m.Type, m.DiameterMm, m.GroundElevationM,
                m.InvertElevationM, m.HeightM, m.Crown, m.Confidence,
                m.CompletenessPercent, m.MissingData, m.ValidationIssues,
                m.SourceDocuments, m.Transitions
            }),
            inlets = project.Inlets.Select(i => new { i.Identifier, i.Confidence, i.SourceDocuments }),
            pipes = project.Pipes.Select(p => new { p.Material, p.DiameterMm, p.Page, p.SourceDocument }),
            warnings = outcome.Warnings
        });
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(StatusCodes.Status408RequestTimeout);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "PrefabScan Web analysis failed");
        return Results.Problem("Analiza nie powiodła się. Sprawdź lokalny dziennik serwera.");
    }
    finally
    {
        limiter.Release();
    }
});

app.MapGet("/api/export/{id:guid}", async Task<IResult> (Guid id, AnalysisStore store) =>
{
    var project = store.Get(id);
    if (project == null)
        return Results.NotFound(new { error = "Wynik wygasł. Ponownie przeanalizuj pliki." });

    var temporaryPath = Path.Combine(Path.GetTempPath(), $"PrefabScan_{Guid.NewGuid():N}.xlsx");
    try
    {
        await Task.Run(() => new XlsxProjectExporter().Export(temporaryPath, project));
        var bytes = await File.ReadAllBytesAsync(temporaryPath);
        return Results.File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"PrefabScan_zestawienie_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "PrefabScan Web export failed");
        return Results.Problem("Eksport XLSX nie powiódł się.");
    }
    finally
    {
        if (File.Exists(temporaryPath))
            File.Delete(temporaryPath);
    }
});

// Deliberate localhost-only listener. This is a work-in-progress standalone web
// interface, not a replacement for the user's existing uncommitted local Web app.
app.Run("http://127.0.0.1:5000");

public partial class Program { }
