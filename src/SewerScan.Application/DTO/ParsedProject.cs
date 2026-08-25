using System.Collections.Generic;

namespace SewerScan.Application.DTO
{
    /// <summary>
    /// Parsed representation of one or more project drawings.
    /// </summary>
    public class ParsedProject
    {
        public string SourceFile { get; set; } = string.Empty;
        public string DrawingType { get; set; } = "NIEZNANY";
        public string Diagnostics { get; set; } = string.Empty;
        public List<string> SourceDocuments { get; } = new();
        public List<ParsedManhole> Manholes { get; } = new();
        public List<ParsedPipe> Pipes { get; } = new();
        public List<ParsedInlet> Inlets { get; } = new();
    }

    public class ParsedManhole
    {
        public int Page { get; set; }
        public string RawText { get; set; } = string.Empty;
        public string? Identifier { get; set; }
        public string? Type { get; set; }
        public int? DiameterMm { get; set; }
        public double? GroundElevationM { get; set; }
        public double? InvertElevationM { get; set; }
        public double? HeightM { get; set; }
        public string? Crown { get; set; }
        public string Confidence { get; set; } = "niska";
        public string SourceDocument { get; set; } = string.Empty;
        public List<string> SourceDocuments { get; } = new();
        public int CompletenessPercent { get; set; }
        public string MissingData { get; set; } = string.Empty;
        public string ValidationIssues { get; set; } = string.Empty;
        public List<ManholeTransition> Transitions { get; } = new();
    }

    public class ManholeTransition
    {
        public string Material { get; set; } = string.Empty;
        public int DiameterMm { get; set; }
        public int Quantity { get; set; }
    }

    public class ParsedInlet
    {
        public int Page { get; set; }
        public string RawText { get; set; } = string.Empty;
        public string? Identifier { get; set; }
        public string Confidence { get; set; } = "niska";
        public string SourceDocument { get; set; } = string.Empty;
        public List<string> SourceDocuments { get; } = new();
    }

    public class ParsedPipe
    {
        public int Page { get; set; }
        public string RawText { get; set; } = string.Empty;
        public int? DiameterMm { get; set; }
        public string? Material { get; set; }
        public string SourceDocument { get; set; } = string.Empty;
    }
}
