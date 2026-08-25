using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using SewerScan.Application.DTO;

namespace SewerScan.Infrastructure.Excel
{
    /// <summary>
    /// Dependency-free XLSX exporter. Creates a standard Open XML workbook with
    /// Studnie, Wpusty and Rury sheets so PrefabScan can export without another NuGet package.
    /// </summary>
    public sealed class XlsxProjectExporter
    {
        public void Export(string filePath, ParsedProject project)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));
            if (project is null) throw new ArgumentNullException(nameof(project));

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            if (File.Exists(filePath)) File.Delete(filePath);

            using var fs = File.Create(filePath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

            WriteTextEntry(zip, "[Content_Types].xml", BuildContentTypes());
            WriteTextEntry(zip, "_rels/.rels", BuildRootRelationships());
            WriteTextEntry(zip, "xl/workbook.xml", BuildWorkbook());
            WriteTextEntry(zip, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationships());
            WriteTextEntry(zip, "xl/styles.xml", BuildStyles());
            WriteTextEntry(zip, "docProps/app.xml", BuildAppProperties());
            WriteTextEntry(zip, "docProps/core.xml", BuildCoreProperties());

            WriteWorksheet(zip, "xl/worksheets/sheet1.xml", BuildManholeRows(project));
            WriteWorksheet(zip, "xl/worksheets/sheet2.xml", BuildInletRows(project));
            WriteWorksheet(zip, "xl/worksheets/sheet3.xml", BuildPipeRows(project));
        }

        private static IEnumerable<object?[]> BuildManholeRows(ParsedProject project)
        {
            yield return new object?[]
            {
                "Oznaczenie", "Typ", "DN [mm]", "Rzędna góry [m]", "Rzędna dna [m]", "Wysokość [m]",
                "Zwieńczenie", "Przejścia szczelne", "Pewność", "Kompletność [%]", "Brakujące dane", "Uwagi walidacji", "Źródła"
            };

            foreach (var m in project.Manholes.OrderBy(m => NaturalKey(m.Identifier)))
            {
                yield return new object?[]
                {
                    m.Identifier,
                    m.Type,
                    m.DiameterMm,
                    m.GroundElevationM,
                    m.InvertElevationM,
                    m.HeightM,
                    m.Crown,
                    string.Join(" | ", m.Transitions.OrderBy(t => t.Material).ThenBy(t => t.DiameterMm)
                        .Select(t => $"{t.Material} DN{t.DiameterMm} × {t.Quantity}")),
                    m.Confidence,
                    m.CompletenessPercent,
                    m.MissingData,
                    m.ValidationIssues,
                    string.Join(" | ", m.SourceDocuments)
                };
            }
        }

        private static IEnumerable<object?[]> BuildInletRows(ParsedProject project)
        {
            yield return new object?[] { "Oznaczenie", "Pewność", "Źródła" };
            foreach (var i in project.Inlets.OrderBy(i => NaturalKey(i.Identifier)))
                yield return new object?[] { i.Identifier, i.Confidence, string.Join(" | ", i.SourceDocuments) };
        }

        private static IEnumerable<object?[]> BuildPipeRows(ParsedProject project)
        {
            yield return new object?[] { "Materiał", "DN [mm]", "Strona", "Źródło", "Tekst źródłowy" };
            foreach (var p in project.Pipes.OrderBy(p => p.DiameterMm).ThenBy(p => p.Material))
                yield return new object?[] { p.Material, p.DiameterMm, p.Page, p.SourceDocument, p.RawText };
        }

        private static string NaturalKey(string? identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return "ZZZ999999";
            var value = identifier.Trim().ToUpperInvariant();
            var prefix = new string(value.TakeWhile(char.IsLetter).ToArray());
            var rest = value[prefix.Length..];
            var chunks = rest.Split(new[] { '/', '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var main = chunks.Length > 0 && int.TryParse(chunks[0], out var m) ? m : 999999;
            var sub = chunks.Length > 1 && int.TryParse(chunks[1], out var s) ? s : -1;
            return $"{prefix}{main:D6}.{sub + 1:D6}";
        }

        private static void WriteWorksheet(ZipArchive zip, string path, IEnumerable<object?[]> rows)
        {
            var materializedRows = rows.ToList();
            var maxColumns = materializedRows.Count == 0 ? 1 : materializedRows.Max(r => r.Length);
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            using var stream = entry.Open();
            var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, CloseOutput = false };
            using var writer = XmlWriter.Create(stream, settings);

            writer.WriteStartDocument(true);
            writer.WriteStartElement("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            writer.WriteStartElement("sheetViews");
            writer.WriteStartElement("sheetView");
            writer.WriteAttributeString("workbookViewId", "0");
            writer.WriteStartElement("pane");
            writer.WriteAttributeString("ySplit", "1");
            writer.WriteAttributeString("topLeftCell", "A2");
            writer.WriteAttributeString("activePane", "bottomLeft");
            writer.WriteAttributeString("state", "frozen");
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteStartElement("sheetData");
            var rowNumber = 0;
            foreach (var row in materializedRows)
            {
                rowNumber++;
                writer.WriteStartElement("row");
                writer.WriteAttributeString("r", rowNumber.ToString(CultureInfo.InvariantCulture));

                for (var col = 0; col < row.Length; col++)
                    WriteCell(writer, rowNumber, col + 1, row[col], rowNumber == 1 ? 1 : 0);

                writer.WriteEndElement();
            }
            writer.WriteEndElement();

            writer.WriteStartElement("autoFilter");
            writer.WriteAttributeString("ref", $"A1:{CellReference(maxColumns, Math.Max(1, rowNumber))}");
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        private static void WriteCell(XmlWriter writer, int row, int column, object? value, int style)
        {
            writer.WriteStartElement("c");
            writer.WriteAttributeString("r", CellReference(column, row));
            writer.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));

            if (value is null)
            {
                writer.WriteAttributeString("t", "inlineStr");
                writer.WriteStartElement("is");
                writer.WriteElementString("t", string.Empty);
                writer.WriteEndElement();
            }
            else if (value is byte or short or int or long or float or double or decimal)
            {
                writer.WriteElementString("v", Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            else
            {
                writer.WriteAttributeString("t", "inlineStr");
                writer.WriteStartElement("is");
                writer.WriteStartElement("t");
                writer.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
                writer.WriteString(Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        private static string CellReference(int column, int row)
        {
            var name = string.Empty;
            var n = column;
            while (n > 0)
            {
                n--;
                name = (char)('A' + (n % 26)) + name;
                n /= 26;
            }
            return name + row.ToString(CultureInfo.InvariantCulture);
        }

        private static void WriteTextEntry(ZipArchive zip, string path, string text)
        {
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(text);
        }

        private static string BuildContentTypes() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
  <Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
  <Override PartName="/xl/worksheets/sheet3.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
  <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
  <Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
</Types>
""";

        private static string BuildRootRelationships() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
</Relationships>
""";

        private static string BuildWorkbook() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets>
    <sheet name="Studnie" sheetId="1" r:id="rId1"/>
    <sheet name="Wpusty" sheetId="2" r:id="rId2"/>
    <sheet name="Rury" sheetId="3" r:id="rId3"/>
  </sheets>
</workbook>
""";

        private static string BuildWorkbookRelationships() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet3.xml"/>
  <Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>
""";

        private static string BuildStyles() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>
  <fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFD9EAF7"/><bgColor indexed="64"/></patternFill></fill></fills>
  <borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style="thin"/><right style="thin"/><top style="thin"/><bottom style="thin"/><diagonal/></border></borders>
  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
  <cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1"/></cellXfs>
  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
</styleSheet>
""";

        private static string BuildCoreProperties() => $"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <dc:creator>PrefabScan</dc:creator><cp:lastModifiedBy>PrefabScan</cp:lastModifiedBy>
  <dcterms:created xsi:type="dcterms:W3CDTF">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:created>
</cp:coreProperties>
""";

        private static string BuildAppProperties() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes"><Application>PrefabScan</Application></Properties>
""";
    }
}
