using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SewerScan.Application.DTO;

namespace SewerScan.Application.Services
{
    /// <summary>
    /// Regression benchmark for the Torun/Batorego reference project.
    /// IMPORTANT: this class never changes ParsedProject. It only compares parser output
    /// with the user's manually prepared offer so development can be measured objectively.
    /// </summary>
    public static class BatoregoBenchmark
    {
        private sealed record ExpectedManhole(string Id, int Dn, double Height, string Type, string Crown);

        private static readonly ExpectedManhole[] Expected =
        {
            new("D1", 1200, 2.21, "KINETA", "zwężka"),
            new("D2", 1200, 2.21, "KINETA", "zwężka"),
            new("D3", 1200, 1.56, "KINETA", "zwężka"),
            new("D4", 1200, 1.04, "KINETA", "pierścień+płyta_odc."),
            new("D6", 1200, 1.57, "KINETA", "zwężka"),
            new("D7", 1200, 1.33, "KINETA", "zwężka"),
            new("D8", 1200, 1.25, "KINETA", "zwężka"),
            new("D9", 1200, 1.73, "KINETA", "zwężka"),
            new("D10", 1200, 1.73, "KINETA", "zwężka"),
            new("D11", 1200, 1.57, "KINETA", "zwężka"),
            new("D12", 1200, 1.56, "KINETA", "zwężka"),
            new("D13", 1200, 1.58, "KINETA", "zwężka"),
            new("S3", 1200, 1.74, "KINETA", "zwężka"),
            new("S4", 1200, 1.70, "KINETA", "zwężka"),
            new("S7", 1200, 0.99, "KINETA", "pierścień+płyta_odc.")
        };

        public static string BuildReport(ParsedProject project)
        {
            var sb = new StringBuilder();
            sb.AppendLine("BENCHMARK REGRESYJNY — TORUŃ, BATOREGO");
            sb.AppendLine("UWAGA: benchmark wyłącznie ocenia wynik. Nie uzupełnia danych parsera.");
            sb.AppendLine();

            var actual = project.Manholes
                .Where(m => !string.IsNullOrWhiteSpace(m.Identifier))
                .GroupBy(m => Normalize(m.Identifier), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var expectedIds = Expected.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var found = 0;
            var dnOk = 0;
            var heightOk = 0;
            var typeOk = 0;
            var crownOk = 0;
            var totalFieldChecks = 0;
            var fieldHits = 0;

            sb.AppendLine("ID       WYKRYTA   DN          H [m]          TYP          ZWIEŃCZENIE");
            sb.AppendLine(new string('-', 88));

            foreach (var expected in Expected)
            {
                if (!actual.TryGetValue(expected.Id, out var manhole))
                {
                    sb.AppendLine($"{expected.Id,-8} NIE      -           -              -            -");
                    totalFieldChecks += 4;
                    continue;
                }

                found++;
                var dnMatch = manhole.DiameterMm == expected.Dn;
                var heightMatch = manhole.HeightM.HasValue && Math.Abs(manhole.HeightM.Value - expected.Height) <= 0.05;
                var typeMatch = EquivalentType(manhole.Type, expected.Type);
                var crownMatch = EquivalentCrown(manhole.Crown, expected.Crown);

                totalFieldChecks += 4;
                fieldHits += (dnMatch ? 1 : 0) + (heightMatch ? 1 : 0) + (typeMatch ? 1 : 0) + (crownMatch ? 1 : 0);
                if (dnMatch) dnOk++;
                if (heightMatch) heightOk++;
                if (typeMatch) typeOk++;
                if (crownMatch) crownOk++;

                var dnText = manhole.DiameterMm.HasValue ? $"{manhole.DiameterMm} {(dnMatch ? "OK" : "!=1200")}" : "brak";
                var hText = manhole.HeightM.HasValue ? $"{manhole.HeightM:0.00} {(heightMatch ? "OK" : $"!={expected.Height:0.00}")}" : "brak";
                var tText = string.IsNullOrWhiteSpace(manhole.Type) ? "brak" : $"{manhole.Type} {(typeMatch ? "OK" : "BŁĄD")}";
                var cText = string.IsNullOrWhiteSpace(manhole.Crown) ? "brak" : $"{manhole.Crown} {(crownMatch ? "OK" : "BŁĄD")}";
                sb.AppendLine($"{expected.Id,-8} TAK      {dnText,-11} {hText,-14} {tText,-12} {cText}");
            }

            var extras = actual.Keys.Where(k => !expectedIds.Contains(k)).OrderBy(k => k).ToList();
            sb.AppendLine();
            sb.AppendLine($"Wykryte oczekiwane studnie: {found}/{Expected.Length} ({Percent(found, Expected.Length)}%)");
            sb.AppendLine($"DN poprawne:               {dnOk}/{Expected.Length} ({Percent(dnOk, Expected.Length)}%)");
            sb.AppendLine($"Wysokości ±0,05 m:         {heightOk}/{Expected.Length} ({Percent(heightOk, Expected.Length)}%)");
            sb.AppendLine($"Typ poprawny:              {typeOk}/{Expected.Length} ({Percent(typeOk, Expected.Length)}%)");
            sb.AppendLine($"Zwieńczenie poprawne:      {crownOk}/{Expected.Length} ({Percent(crownOk, Expected.Length)}%)");
            sb.AppendLine($"Trafność pól technicznych: {fieldHits}/{totalFieldChecks} ({Percent(fieldHits, totalFieldChecks)}%)");
            sb.AppendLine($"Fałszywe/dodatkowe ID:     {extras.Count}" + (extras.Count > 0 ? $" -> {string.Join(", ", extras)}" : string.Empty));
            sb.AppendLine();
            sb.AppendLine("Oczekiwane wpusty z oferty (kontrola informacyjna): WP ×3, WP1 ×4, WP2 ×1; DN500; OSADNIK.");
            return sb.ToString();
        }



        public static string BuildCompactDetail(ParsedProject project)
        {
            var actualIds = project.Manholes
                .Where(m => !string.IsNullOrWhiteSpace(m.Identifier))
                .Select(m => Normalize(m.Identifier))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var expectedIds = Expected.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = expectedIds.Where(id => !actualIds.Contains(id)).OrderBy(SortId).ToList();
            var extras = actualIds.Where(id => !expectedIds.Contains(id)).OrderBy(SortId).ToList();

            return "Brakujące ID: " + (missing.Count == 0 ? "brak" : string.Join(", ", missing)) +
                   " | Dodatkowe ID: " + (extras.Count == 0 ? "brak" : string.Join(", ", extras));
        }

        private static string SortId(string id)
        {
            var m = System.Text.RegularExpressions.Regex.Match(id ?? string.Empty, @"^(?<p>[A-Z]+)(?<n>\d+)?");
            if (!m.Success) return id ?? string.Empty;
            var n = int.TryParse(m.Groups["n"].Value, out var value) ? value : 99999;
            return $"{m.Groups["p"].Value}{n:D5}";
        }


        public static (int Found, int Dn, int Height, int Type, int Crown, int Extras) GetScore(ParsedProject project)
        {
            var actual = project.Manholes
                .Where(m => !string.IsNullOrWhiteSpace(m.Identifier))
                .GroupBy(m => Normalize(m.Identifier), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var expectedIds = Expected.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var found = 0;
            var dn = 0;
            var height = 0;
            var type = 0;
            var crown = 0;

            foreach (var expected in Expected)
            {
                if (!actual.TryGetValue(expected.Id, out var manhole))
                    continue;

                found++;
                if (manhole.DiameterMm == expected.Dn) dn++;
                if (manhole.HeightM.HasValue && Math.Abs(manhole.HeightM.Value - expected.Height) <= 0.05) height++;
                if (EquivalentType(manhole.Type, expected.Type)) type++;
                if (EquivalentCrown(manhole.Crown, expected.Crown)) crown++;
            }

            var extras = actual.Keys.Count(k => !expectedIds.Contains(k));
            return (found, dn, height, type, crown, extras);
        }

        public static string BuildCompactSummary(ParsedProject project)
        {
            var actual = project.Manholes
                .Where(m => !string.IsNullOrWhiteSpace(m.Identifier))
                .GroupBy(m => Normalize(m.Identifier), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var expectedIds = Expected.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var found = 0;
            var dnOk = 0;
            var heightOk = 0;
            var typeOk = 0;
            var crownOk = 0;

            foreach (var expected in Expected)
            {
                if (!actual.TryGetValue(expected.Id, out var manhole))
                    continue;

                found++;
                if (manhole.DiameterMm == expected.Dn) dnOk++;
                if (manhole.HeightM.HasValue && Math.Abs(manhole.HeightM.Value - expected.Height) <= 0.05) heightOk++;
                if (EquivalentType(manhole.Type, expected.Type)) typeOk++;
                if (EquivalentCrown(manhole.Crown, expected.Crown)) crownOk++;
            }

            var extras = actual.Keys.Count(k => !expectedIds.Contains(k));
            return $"BENCHMARK BATOREGO: ID {found}/{Expected.Length} | DN {dnOk}/{Expected.Length} | H {heightOk}/{Expected.Length} | typ {typeOk}/{Expected.Length} | zwieńczenie {crownOk}/{Expected.Length} | dodatkowe ID {extras}";
        }

        private static int Percent(int value, int total) => total <= 0 ? 0 : (int)Math.Round(value * 100.0 / total);
        private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", string.Empty).Replace("\\", "/");

        private static bool EquivalentType(string? actual, string expected)
        {
            var a = NormalizeLoose(actual);
            var e = NormalizeLoose(expected);
            return a.Contains(e, StringComparison.OrdinalIgnoreCase) || e.Contains(a, StringComparison.OrdinalIgnoreCase) && a.Length >= 4;
        }

        private static bool EquivalentCrown(string? actual, string expected)
        {
            var a = NormalizeLoose(actual);
            var e = NormalizeLoose(expected);
            if (string.IsNullOrWhiteSpace(a)) return false;
            if (e.Contains("zwezka")) return a.Contains("zwezka");
            if (e.Contains("pierscien") && e.Contains("plyta")) return a.Contains("pierscien") && a.Contains("plyta");
            return a.Contains(e) || e.Contains(a);
        }

        private static string NormalizeLoose(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return value.Trim().ToLowerInvariant()
                .Replace("ą", "a").Replace("ć", "c").Replace("ę", "e").Replace("ł", "l")
                .Replace("ń", "n").Replace("ó", "o").Replace("ś", "s").Replace("ź", "z").Replace("ż", "z")
                .Replace("_", string.Empty).Replace(" ", string.Empty).Replace(".", string.Empty).Replace("+", string.Empty);
        }
    }
}
