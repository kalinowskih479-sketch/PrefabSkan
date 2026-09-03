using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using SewerScan.Application.DTO;
using SewerScan.Application.Services;
using SewerScan.Infrastructure.Excel;
using SewerScan.Infrastructure.Parsers;
using SewerScan.Infrastructure.Pdf;
using SewerScan.Shared.Utilities;

namespace SewerScan.UI;

public partial class MainWindow : Window
{
    private readonly List<string> _selectedPdfs = new();
    private ParsedProject? _lastProject;
    private string _lastDiagnostics = string.Empty;
    private bool _benchmarkAutoRun;

    public MainWindow()
    {
        InitializeComponent();

        // Unattended regression mode: RUN_BATOREGO_TEST.bat starts the application
        // with --benchmark, so the complete reference analysis begins automatically.
        if (Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--benchmark", StringComparison.OrdinalIgnoreCase)))
        {
            Loaded += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                    BenchmarkTestButton_Click(this, new RoutedEventArgs())));
            };
        }
    }

    private void OpenPdfButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Wybierz dokumentację PDF projektu",
            Filter = "Pliki PDF (*.pdf)|*.pdf",
            CheckFileExists = true,
            Multiselect = true
        };

        if (dialog.ShowDialog() != true)
            return;

        _selectedPdfs.Clear();
        _selectedPdfs.AddRange(dialog.FileNames.Distinct(StringComparer.OrdinalIgnoreCase));
        FilePathBox.Text = string.Join(Environment.NewLine, _selectedPdfs.Select(Path.GetFileName));
        AnalyzeButton.IsEnabled = _selectedPdfs.Count > 0;
        ExportButton.IsEnabled = false;
        ExportDiagnosticsButton.IsEnabled = false;
        _lastProject = null;
        _lastDiagnostics = string.Empty;
        BenchmarkText.Text = "Benchmark: oczekuje na analizę.";
        StatusText.Text = $"Wybrano {_selectedPdfs.Count} plik(ów). Kliknij Analizuj zestaw.";
    }

    private static string GetIdentifierSortKey(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return "ZZZ999999";

        var m = System.Text.RegularExpressions.Regex.Match(
            identifier.Trim().ToUpperInvariant(),
            @"^(?<p>[A-Z]+)(?<n>\d+)?(?:[./-](?<s>\d+))?$");

        if (!m.Success)
            return identifier.ToUpperInvariant();

        var prefix = m.Groups["p"].Value;
        var n = int.TryParse(m.Groups["n"].Value, out var main) ? main : 999999;
        var sub = int.TryParse(m.Groups["s"].Value, out var secondary) ? secondary : -1;
        return $"{prefix}{n:D6}.{sub + 1:D6}";
    }

    private static string NormalizeInletIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return string.Empty;

        var value = identifier.Trim().ToUpperInvariant().Replace(" ", string.Empty);
        return value.StartsWith("WP", StringComparison.OrdinalIgnoreCase) ? value : "WP" + value;
    }

    private void BenchmarkTestButton_Click(object sender, RoutedEventArgs e)
    {
        var referenceDir = Path.Combine(AppContext.BaseDirectory, "Reference", "Batorego");
        var required = new[]
        {
            "Rys I_1 PZT.pdf",
            "Rys I_4 profil kanalizacja sanitarna.pdf",
            "Rys I_8 profil kanalizacja deszczowa.pdf"
        };

        var files = required.Select(name => Path.Combine(referenceDir, name)).ToList();
        var missing = files.Where(path => !File.Exists(path)).ToList();
        if (missing.Count > 0)
        {
            MessageBox.Show(
                "Brakuje referencyjnych plików testowych obok programu:" + Environment.NewLine +
                string.Join(Environment.NewLine, missing.Select(Path.GetFileName)) + Environment.NewLine + Environment.NewLine +
                $"Przebuduj rozwiązanie {ProductInfo.Version} — pliki Reference/Batorego powinny być kopiowane automatycznie do katalogu wyjściowego.",
                $"{ProductInfo.DisplayName} - brak danych testowych",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _selectedPdfs.Clear();
        _selectedPdfs.AddRange(files);
        FilePathBox.Text = string.Join(Environment.NewLine, files.Select(Path.GetFileName));
        AnalyzeButton.IsEnabled = true;
        _benchmarkAutoRun = true;
        BenchmarkText.Text = "BENCHMARK BATOREGO: automatyczny test w toku…";
        StatusText.Text = "Uruchamiam automatyczny test regresyjny Batorego.";
        AnalyzeButton_Click(BenchmarkTestButton, new RoutedEventArgs());
    }

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPdfs.Count == 0)
            return;

        AnalyzeButton.IsEnabled = false;
        AnalyzeButton.Content = "Analizuję…";
        OpenPdfButton.IsEnabled = false;
        BenchmarkTestButton.IsEnabled = false;
        ExportButton.IsEnabled = false;
        ExportDiagnosticsButton.IsEnabled = false;
        System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

        try
        {
            var analyzer = new PdfAnalyzer(new PdfTextExtractor(), new SewerProjectParser());
            var projects = new List<ParsedProject>();
            var skippedFiles = new List<string>();

            for (var i = 0; i < _selectedPdfs.Count; i++)
            {
                var path = _selectedPdfs[i];
                StatusText.Text = $"Analizuję {i + 1}/{_selectedPdfs.Count}: {Path.GetFileName(path)} — jeśli PDF nie ma tekstu, PrefabScan uruchomi OCR.";
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                try
                {
                    // PdfPig extraction is CPU-bound and the extractor currently exposes an async
                    // signature over synchronous work. Run the whole per-file analysis on a worker
                    // thread so the WPF UI remains responsive and status updates are visible.
                    var project = await Task.Run(() => analyzer.AnalyzeAsync(path));
                    projects.Add(project);
                }
                catch (Exception fileException)
                {
                    skippedFiles.Add($"{Path.GetFileName(path)} — {GetShortError(fileException)}");
                }
            }

            if (projects.Count == 0)
            {
                var report = "Nie udało się odczytać żadnego z wybranych PDF-ów." + Environment.NewLine + Environment.NewLine +
                             string.Join(Environment.NewLine, skippedFiles.Select(x => "• " + x));
                StatusText.Text = "Nie udało się odczytać wybranych PDF-ów.";
                MessageBox.Show(report, "PrefabScan - PDF nieczytelny", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _lastProject = ProjectMerger.Merge(projects);
            if (skippedFiles.Count > 0)
            {
                _lastProject.Diagnostics += Environment.NewLine + Environment.NewLine +
                    "POMINIĘTE PLIKI:" + Environment.NewLine +
                    string.Join(Environment.NewLine, skippedFiles.Select(x => "- " + x));
            }

            BindProject(_lastProject);
            ExportButton.IsEnabled = true;
            _lastDiagnostics = BuildDiagnostics(_lastProject);
            ExportDiagnosticsButton.IsEnabled = true;
            BenchmarkText.Text = BatoregoBenchmark.BuildCompactSummary(_lastProject) + Environment.NewLine + BatoregoBenchmark.BuildCompactDetail(_lastProject);

            if (_benchmarkAutoRun)
            {
                var reportDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PrefabScan",
                    "Benchmark");
                Directory.CreateDirectory(reportDir);
                var reportPath = Path.Combine(reportDir, $"Batorego_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                var latestPath = Path.Combine(reportDir, "Batorego_LATEST.txt");
                File.WriteAllText(reportPath, _lastDiagnostics, System.Text.Encoding.UTF8);
                File.WriteAllText(latestPath, _lastDiagnostics, System.Text.Encoding.UTF8);

                var score = BatoregoBenchmark.GetScore(_lastProject);
                var historyPath = Path.Combine(reportDir, "Batorego_HISTORY.csv");
                if (!File.Exists(historyPath))
                    File.WriteAllText(historyPath, "timestamp;version;id;dn;height;type;crown;extras" + Environment.NewLine, System.Text.Encoding.UTF8);
                File.AppendAllText(
                    historyPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss};{ProductInfo.Version};{score.Found};{score.Dn};{score.Height};{score.Type};{score.Crown};{score.Extras}" + Environment.NewLine,
                    System.Text.Encoding.UTF8);

                var clipboardResult =
                    BatoregoBenchmark.BuildCompactSummary(_lastProject) + Environment.NewLine +
                    BatoregoBenchmark.BuildCompactDetail(_lastProject);
                try { Clipboard.SetText(clipboardResult); } catch { /* clipboard is convenience only */ }

                StatusText.Text = $"Test Batorego zakończony. Raport: {reportPath}. Skrót wyniku skopiowano do schowka.";
            }

            var complete = _lastProject.Manholes.Count(m => m.CompletenessPercent >= 70);
            var skippedSuffix = skippedFiles.Count > 0 ? $"; pominięte PDF-y: {skippedFiles.Count}" : string.Empty;
            var standardStatus =
                $"Gotowe: {_lastProject.DrawingType}; dokumenty {_lastProject.SourceDocuments.Count}; " +
                $"studnie {_lastProject.Manholes.Count} (≥70% kompletności: {complete}), " +
                $"wpusty {_lastProject.Inlets.Count}, rury {_lastProject.Pipes.Count}{skippedSuffix}.";
            if (!_benchmarkAutoRun)
                StatusText.Text = standardStatus;
            else
                StatusText.Text = "TEST BATOREGO — " + BatoregoBenchmark.BuildCompactSummary(_lastProject);

            if (_lastProject.Manholes.Count == 0 && _lastProject.Inlets.Count == 0 && _lastProject.Pipes.Count == 0)
            {
                ResultsTabs.SelectedItem = DiagnosticsTab;
                StatusText.Text += " Brak obiektów — otwarto Diagnostykę. Zapisz raport TXT.";
            }

            if (skippedFiles.Count > 0)
            {
                MessageBox.Show(
                    "Analiza zakończona, ale część plików została pominięta:" + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, skippedFiles.Select(x => "• " + x)) + Environment.NewLine + Environment.NewLine +
                    "Pozostałe dokumenty zostały przeanalizowane normalnie. Szczegóły są też w zakładce Diagnostyka.",
                    "PrefabScan - częściowa analiza",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Błąd analizy.";
            MessageBox.Show(ex.ToString(), "PrefabScan - błąd", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            System.Windows.Input.Mouse.OverrideCursor = null;
            AnalyzeButton.Content = "Analizuj zestaw";
            AnalyzeButton.IsEnabled = _selectedPdfs.Count > 0;
            OpenPdfButton.IsEnabled = true;
            BenchmarkTestButton.IsEnabled = true;
            _benchmarkAutoRun = false;
        }
    }

    private static string GetShortError(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null && current is not InvalidDataException)
            current = current.InnerException;

        var message = current.Message?.Trim();
        if (string.IsNullOrWhiteSpace(message))
            message = exception.GetType().Name;

        const int maxLength = 240;
        return message.Length <= maxLength ? message : message[..maxLength] + "…";
    }

    private void BindProject(ParsedProject project)
    {
        ManholesGrid.ItemsSource = project.Manholes
            .OrderBy(m => GetIdentifierSortKey(m.Identifier))
            .Select(m => new
            {
                m.Identifier,
                m.Type,
                m.DiameterMm,
                GroundElevationM = m.GroundElevationM?.ToString("0.00"),
                InvertElevationM = m.InvertElevationM?.ToString("0.00"),
                HeightM = m.HeightM?.ToString("0.00"),
                m.Crown,
                TransitionsText = string.Join(" | ", m.Transitions.Select(t => $"{t.Material} DN{t.DiameterMm} × {t.Quantity}")),
                m.Confidence,
                Completeness = $"{m.CompletenessPercent}%",
                m.MissingData,
                m.ValidationIssues,
                Sources = string.Join(" | ", m.SourceDocuments)
            }).ToList();

        InletsGrid.ItemsSource = project.Inlets
            .OrderBy(i => GetIdentifierSortKey(NormalizeInletIdentifier(i.Identifier)))
            .Select(i => new
            {
                Identifier = NormalizeInletIdentifier(i.Identifier),
                i.Confidence,
                Sources = string.Join(" | ", i.SourceDocuments)
            }).ToList();

        PipesGrid.ItemsSource = project.Pipes
            .OrderBy(p => p.DiameterMm)
            .ThenBy(p => p.Material)
            .ToList();

        DiagnosticsBox.Text = BuildDiagnostics(project);
        _lastDiagnostics = DiagnosticsBox.Text;
    }

    private static string BuildDiagnostics(ParsedProject project)
    {
        var summary = new System.Text.StringBuilder();
        summary.AppendLine($"{ProductInfo.DisplayName.ToUpperInvariant()} — RAPORT DIAGNOSTYCZNY");
        summary.AppendLine($"Typ zestawu: {project.DrawingType}");
        summary.AppendLine($"Dokumenty: {string.Join(" | ", project.SourceDocuments)}");
        summary.AppendLine();
        summary.AppendLine($"Studnie: {project.Manholes.Count} | Wpusty: {project.Inlets.Count} | Rury: {project.Pipes.Count}");
        summary.AppendLine($"Studnie kompletne (100%): {project.Manholes.Count(m => m.CompletenessPercent >= 100)} | Do weryfikacji: {project.Manholes.Count(m => !string.IsNullOrWhiteSpace(m.ValidationIssues))}");
        summary.AppendLine();
        summary.AppendLine("BRAKI DANYCH W STUDNIACH:");

        foreach (var m in project.Manholes.OrderBy(m => GetIdentifierSortKey(m.Identifier)))
            summary.AppendLine($"{m.Identifier,-10} {m.CompletenessPercent,3}%  pewność: {m.Confidence,-7}  braki: {m.MissingData}  uwagi: {m.ValidationIssues}");

        summary.AppendLine();
        summary.AppendLine(BatoregoBenchmark.BuildReport(project));
        summary.AppendLine();
        summary.AppendLine("SUROWA DIAGNOSTYKA PARSERÓW:");
        summary.AppendLine(project.Diagnostics);
        return summary.ToString();
    }

    private void ExportDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastDiagnostics))
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Eksport diagnostyki PrefabScan",
            Filter = "Plik tekstowy (*.txt)|*.txt",
            DefaultExt = ".txt",
            AddExtension = true,
            FileName = $"PrefabScan_diagnostyka_{DateTime.Now:yyyyMMdd_HHmm}.txt"
        };

        if (dialog.ShowDialog() != true)
            return;

        File.WriteAllText(dialog.FileName, _lastDiagnostics, System.Text.Encoding.UTF8);
        StatusText.Text = $"Wyeksportowano diagnostykę: {Path.GetFileName(dialog.FileName)}";
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastProject is null)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Eksport zestawienia PrefabScan",
            Filter = "Excel (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            FileName = $"PrefabScan_zestawienie_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            new XlsxProjectExporter().Export(dialog.FileName, _lastProject);
            StatusText.Text = $"Wyeksportowano Excel: {Path.GetFileName(dialog.FileName)}";
            MessageBox.Show("Zestawienie zostało zapisane.", "PrefabScan", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "PrefabScan - błąd eksportu", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
