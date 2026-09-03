using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SewerScan.Application.DTO;

namespace SewerScan.Application.Services
{
    /// <summary>
    /// Merges complementary PZT/profile/detail analyses by object identifier.
    /// Values already supported by a source are preserved; missing values are filled
    /// from other drawings. Conflicting values are not silently averaged.
    /// </summary>
    public static class ProjectMerger
    {
        public static ParsedProject Merge(IEnumerable<ParsedProject> projects)
        {
            if (projects is null) throw new ArgumentNullException(nameof(projects));

            var inputs = projects
                .Where(p => p != null)
                .OrderBy(p => string.Equals(p.DrawingType, "PZT", StringComparison.OrdinalIgnoreCase) ? 0 :
                              string.Equals(p.DrawingType, "PROFIL", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                .ToList();
            var merged = new ParsedProject
            {
                DrawingType = BuildDrawingType(inputs),
                SourceFile = string.Join(Environment.NewLine, inputs.Select(p => p.SourceFile).Where(s => !string.IsNullOrWhiteSpace(s))),
                Diagnostics = string.Join(Environment.NewLine + Environment.NewLine, inputs.Select(p =>
                    $"===== {Path.GetFileName(p.SourceFile)} | {p.DrawingType} ====={Environment.NewLine}{p.Diagnostics}").Where(s => !string.IsNullOrWhiteSpace(s)))
            };

            foreach (var source in inputs.SelectMany(p => p.SourceDocuments)
                         .Concat(inputs.Select(p => Path.GetFileName(p.SourceFile)))
                         .Where(s => !string.IsNullOrWhiteSpace(s))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                merged.SourceDocuments.Add(source);
            }

            // Vision 3.4: when a PZT is present and yields a useful object inventory,
            // use it as the primary identity map. Profiles are excellent parameter sources,
            // but OCR on dense profile tables can hallucinate identifiers from values such
            // as DN40 / 69,8. A profile-only identifier is therefore accepted only when it
            // carries strong engineering evidence.
            var pztInventory = inputs
                .Where(p => string.Equals(p.DrawingType, "PZT", StringComparison.OrdinalIgnoreCase))
                .SelectMany(p => p.Manholes)
                .Select(m => NormalizeIdentifier(m.Identifier))
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var usePztInventory = pztInventory.Count >= 6;

            var profileOccurrences = inputs
                .Where(p => string.Equals(p.DrawingType, "PROFIL", StringComparison.OrdinalIgnoreCase))
                .SelectMany(p => p.Manholes.Select(m => new { Project = p, Manhole = m }))
                .Where(x => !string.IsNullOrWhiteSpace(x.Manhole.Identifier))
                .GroupBy(x => NormalizeIdentifier(x.Manhole.Identifier), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => Path.GetFileName(x.Project.SourceFile)).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var sourceProject in inputs)
            foreach (var sourceManhole in sourceProject.Manholes)
            {
                var key = NormalizeIdentifier(sourceManhole.Identifier);

                if (usePztInventory &&
                    !string.Equals(sourceProject.DrawingType, "PZT", StringComparison.OrdinalIgnoreCase) &&
                    !pztInventory.Contains(key))
                {
                    // 4.1 balanced identity rule:
                    // PZT remains the main map, but a profile-only object can be restored when
                    // the profile carries real engineering data or the same ID is seen on more
                    // than one profile. This prevents a single PZT OCR miss from hiding a real
                    // structure while still rejecting bare OCR tokens such as S40.
                    var fieldGroups = 0;
                    if (sourceManhole.DiameterMm.HasValue) fieldGroups++;
                    if (sourceManhole.GroundElevationM.HasValue && sourceManhole.InvertElevationM.HasValue) fieldGroups++;
                    if (!string.IsNullOrWhiteSpace(sourceManhole.Type) || !string.IsNullOrWhiteSpace(sourceManhole.Crown)) fieldGroups++;
                    if (sourceManhole.Transitions.Count > 0) fieldGroups++;

                    var corroboratedByProfiles = profileOccurrences.TryGetValue(key, out var occurrences) && occurrences >= 2;
                    if (fieldGroups < 2 && !corroboratedByProfiles)
                        continue;
                }

                ParsedManhole? target = null;

                if (!string.IsNullOrWhiteSpace(key))
                {
                    target = merged.Manholes.FirstOrDefault(m =>
                        string.Equals(NormalizeIdentifier(m.Identifier), key, StringComparison.OrdinalIgnoreCase));
                }

                if (target is null)
                {
                    target = CloneManhole(sourceManhole);
                    merged.Manholes.Add(target);
                }
                else
                {
                    MergeManhole(target, sourceManhole, sourceProject.DrawingType);
                }
            }

            foreach (var manhole in merged.Manholes)
            {
                if (!manhole.HeightM.HasValue && manhole.GroundElevationM.HasValue && manhole.InvertElevationM.HasValue)
                {
                    var computed = Math.Round(manhole.GroundElevationM.Value - manhole.InvertElevationM.Value, 2);
                    if (computed > 0 && computed < 30)
                        manhole.HeightM = computed;
                }

                CalculateCompleteness(manhole);
                UpgradeConfidenceWhenCorroborated(manhole);
            }

            foreach (var sourceInlet in inputs.SelectMany(p => p.Inlets))
            {
                var key = NormalizeInletIdentifier(sourceInlet.Identifier);
                var target = merged.Inlets.FirstOrDefault(i =>
                    string.Equals(NormalizeInletIdentifier(i.Identifier), key, StringComparison.OrdinalIgnoreCase));

                if (target is null)
                {
                    target = new ParsedInlet
                    {
                        Page = sourceInlet.Page,
                        RawText = sourceInlet.RawText,
                        Identifier = key,
                        Confidence = sourceInlet.Confidence,
                        SourceDocument = sourceInlet.SourceDocument
                    };
                    AddSources(target.SourceDocuments, sourceInlet.SourceDocuments, sourceInlet.SourceDocument);
                    merged.Inlets.Add(target);
                }
                else
                {
                    if (ConfidenceRank(sourceInlet.Confidence) > ConfidenceRank(target.Confidence))
                        target.Confidence = sourceInlet.Confidence;
                    AddSources(target.SourceDocuments, sourceInlet.SourceDocuments, sourceInlet.SourceDocument);
                    AppendRawText(target, sourceInlet.RawText);
                }
            }

            // Pipes are deliberately de-duplicated conservatively. The same material/DN/raw label
            // can appear on PZT and profile; keeping one copy avoids multiplying transitions later.
            foreach (var pipe in inputs.SelectMany(p => p.Pipes))
            {
                var exists = merged.Pipes.Any(p =>
                    p.DiameterMm == pipe.DiameterMm &&
                    string.Equals(p.Material, pipe.Material, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(NormalizeLooseText(p.RawText), NormalizeLooseText(pipe.RawText), StringComparison.OrdinalIgnoreCase));

                if (!exists)
                {
                    merged.Pipes.Add(new ParsedPipe
                    {
                        Page = pipe.Page,
                        RawText = pipe.RawText,
                        DiameterMm = pipe.DiameterMm,
                        Material = pipe.Material,
                        SourceDocument = pipe.SourceDocument
                    });
                }
            }

            return merged;
        }

        private static string BuildDrawingType(IReadOnlyList<ParsedProject> inputs)
        {
            var types = inputs.Select(p => p.DrawingType)
                .Where(t => !string.IsNullOrWhiteSpace(t) && !string.Equals(t, "NIEZNANY", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return types.Count switch
            {
                0 => "NIEZNANY",
                1 => types[0],
                _ => "ZESTAW: " + string.Join(" + ", types)
            };
        }

        private static ParsedManhole CloneManhole(ParsedManhole source)
        {
            var copy = new ParsedManhole
            {
                Page = source.Page,
                RawText = source.RawText,
                Identifier = NormalizeIdentifier(source.Identifier),
                Type = source.Type,
                DiameterMm = source.DiameterMm,
                GroundElevationM = source.GroundElevationM,
                InvertElevationM = source.InvertElevationM,
                HeightM = source.HeightM,
                Crown = source.Crown,
                Confidence = source.Confidence,
                SourceDocument = source.SourceDocument,
                ValidationIssues = source.ValidationIssues
            };

            AddSources(copy.SourceDocuments, source.SourceDocuments, source.SourceDocument);
            foreach (var transition in source.Transitions)
            {
                copy.Transitions.Add(new ManholeTransition
                {
                    Material = transition.Material,
                    DiameterMm = transition.DiameterMm,
                    Quantity = transition.Quantity
                });
            }

            return copy;
        }

        private static void MergeManhole(ParsedManhole target, ParsedManhole source, string sourceDrawingType)
        {
            target.Page = target.Page > 0 ? target.Page : source.Page;
            target.Type = PreferText(target.Type, source.Type);

            if (target.DiameterMm.HasValue && source.DiameterMm.HasValue && target.DiameterMm.Value != source.DiameterMm.Value)
                AddIssue(target, $"sprzeczne DN: {target.DiameterMm} / {source.DiameterMm}");
            else
                target.DiameterMm = PreferNullable(target.DiameterMm, source.DiameterMm);

            var incomingProfilePair =
                string.Equals(sourceDrawingType, "PROFIL", StringComparison.OrdinalIgnoreCase) &&
                source.GroundElevationM.HasValue &&
                source.InvertElevationM.HasValue &&
                source.GroundElevationM.Value > source.InvertElevationM.Value &&
                source.GroundElevationM.Value - source.InvertElevationM.Value is >= 0.35 and <= 8.0;

            if (incomingProfilePair)
            {
                // Longitudinal profile is the preferred source for vertical parameters.
                target.GroundElevationM = source.GroundElevationM;
                target.InvertElevationM = source.InvertElevationM;
                target.HeightM = source.HeightM ??
                    Math.Round(source.GroundElevationM!.Value - source.InvertElevationM!.Value, 2);
            }
            else
            {
                MergeElevation(target, source.GroundElevationM, true);
                MergeElevation(target, source.InvertElevationM, false);

                if (!target.HeightM.HasValue)
                    target.HeightM = source.HeightM;
                else if (source.HeightM.HasValue && Math.Abs(target.HeightM.Value - source.HeightM.Value) > 0.05)
                    AddIssue(target, $"sprzeczna wysokość: {target.HeightM:0.00} / {source.HeightM:0.00}");
            }

            target.Crown = PreferText(target.Crown, source.Crown);

            if (ConfidenceRank(source.Confidence) > ConfidenceRank(target.Confidence))
                target.Confidence = source.Confidence;

            AddSources(target.SourceDocuments, source.SourceDocuments, source.SourceDocument);
            AppendRawText(target, source.RawText);

            foreach (var incoming in source.Transitions)
            {
                var existing = target.Transitions.FirstOrDefault(t =>
                    t.DiameterMm == incoming.DiameterMm &&
                    string.Equals(NormalizeMaterial(t.Material), NormalizeMaterial(incoming.Material), StringComparison.OrdinalIgnoreCase));

                if (existing is null)
                {
                    target.Transitions.Add(new ManholeTransition
                    {
                        Material = NormalizeMaterial(incoming.Material),
                        DiameterMm = incoming.DiameterMm,
                        Quantity = incoming.Quantity
                    });
                }
                else
                {
                    // Same connection may be described on several drawings; never sum duplicates blindly.
                    existing.Quantity = Math.Max(existing.Quantity, incoming.Quantity);
                }
            }

            if (!string.IsNullOrWhiteSpace(target.ValidationIssues))
                target.Confidence = "niska";
        }

        private static void CalculateCompleteness(ParsedManhole manhole)
        {
            var missing = new List<string>();
            var score = 0;
            const int total = 8;

            if (!string.IsNullOrWhiteSpace(manhole.Identifier)) score++; else missing.Add("oznaczenie");
            if (manhole.DiameterMm.HasValue) score++; else missing.Add("DN studni");
            if (manhole.GroundElevationM.HasValue) score++; else missing.Add("rzędna góry");
            if (manhole.InvertElevationM.HasValue) score++; else missing.Add("rzędna dna");
            if (manhole.HeightM.HasValue) score++; else missing.Add("wysokość");
            if (!string.IsNullOrWhiteSpace(manhole.Type)) score++; else missing.Add("typ");
            if (!string.IsNullOrWhiteSpace(manhole.Crown)) score++; else missing.Add("zwieńczenie");
            if (manhole.Transitions.Count > 0) score++; else missing.Add("przejścia szczelne");

            manhole.CompletenessPercent = (int)Math.Round(score * 100.0 / total);
            manhole.MissingData = string.Join(", ", missing);
        }

        private static void UpgradeConfidenceWhenCorroborated(ParsedManhole manhole)
        {
            var sources = manhole.SourceDocuments.Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (sources >= 2 && manhole.CompletenessPercent >= 70 && string.IsNullOrWhiteSpace(manhole.ValidationIssues) && ConfidenceRank(manhole.Confidence) < 3)
                manhole.Confidence = "wysoka";
        }


        private static void MergeElevation(ParsedManhole target, double? incoming, bool ground)
        {
            var current = ground ? target.GroundElevationM : target.InvertElevationM;
            if (!current.HasValue)
            {
                if (ground) target.GroundElevationM = incoming;
                else target.InvertElevationM = incoming;
                return;
            }

            if (incoming.HasValue && Math.Abs(current.Value - incoming.Value) > 0.03)
            {
                AddIssue(target, $"sprzeczna {(ground ? "rzędna góry" : "rzędna dna")}: {current:0.00} / {incoming:0.00}");
            }
        }

        private static void AddIssue(ParsedManhole target, string issue)
        {
            if (string.IsNullOrWhiteSpace(issue)) return;
            if (string.IsNullOrWhiteSpace(target.ValidationIssues)) target.ValidationIssues = issue;
            else if (!target.ValidationIssues.Contains(issue, StringComparison.OrdinalIgnoreCase)) target.ValidationIssues += "; " + issue;
            target.Confidence = "niska";
        }

        private static string? PreferText(string? current, string? incoming)
            => !string.IsNullOrWhiteSpace(current) ? current : incoming;

        private static T? PreferNullable<T>(T? current, T? incoming) where T : struct
            => current.HasValue ? current : incoming;

        private static void AddSources(List<string> target, IEnumerable<string> sources, string? single)
        {
            foreach (var source in sources.Concat(new[] { single ?? string.Empty })
                         .Where(s => !string.IsNullOrWhiteSpace(s))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!target.Contains(source, StringComparer.OrdinalIgnoreCase))
                    target.Add(source);
            }
        }

        private static void AppendRawText(ParsedManhole target, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            if (string.IsNullOrWhiteSpace(target.RawText)) target.RawText = raw;
            else if (!target.RawText.Contains(raw, StringComparison.OrdinalIgnoreCase)) target.RawText += " | " + raw;
        }

        private static void AppendRawText(ParsedInlet target, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            if (string.IsNullOrWhiteSpace(target.RawText)) target.RawText = raw;
            else if (!target.RawText.Contains(raw, StringComparison.OrdinalIgnoreCase)) target.RawText += " | " + raw;
        }

        private static string NormalizeIdentifier(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return value.Trim().ToUpperInvariant().Replace(" ", string.Empty).Replace("\\", "/");
        }

        private static string NormalizeInletIdentifier(string? value)
        {
            var normalized = NormalizeIdentifier(value);
            if (string.IsNullOrWhiteSpace(normalized)) return normalized;
            return normalized.StartsWith("WP", StringComparison.OrdinalIgnoreCase) ? normalized : "WP" + normalized;
        }

        private static string NormalizeMaterial(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var v = value.Trim().ToUpperInvariant().Replace("PEHD", "PE-HD").Replace("HDPE", "PE-HD");
            return v;
        }

        private static string NormalizeLooseText(string? value)
            => string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

        private static int ConfidenceRank(string? confidence)
        {
            if (string.Equals(confidence, "wysoka", StringComparison.OrdinalIgnoreCase)) return 3;
            if (string.Equals(confidence, "średnia", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(confidence, "srednia", StringComparison.OrdinalIgnoreCase)) return 2;
            return 1;
        }
    }
}
