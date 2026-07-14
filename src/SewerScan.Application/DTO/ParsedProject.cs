using System.Collections.Generic;

namespace SewerScan.Application.DTO
{
    /// <summary>
    /// Parsed representation of a project extracted from PDF.
    /// </summary>
    public class ParsedProject
    {
        public string SourceFile { get; set; } = string.Empty;
        public List<ParsedManhole> Manholes { get; } = new();
        public List<ParsedPipe> Pipes { get; } = new();
        public List<ParsedInlet> Inlets { get; } = new();
    }

    public class ParsedManhole
    {
        public int Page { get; set; }
        public string RawText { get; set; } = string.Empty;
    }

    public class ParsedInlet
    {
        public int Page { get; set; }
        public string RawText { get; set; } = string.Empty;
    }

    public class ParsedPipe
    {
        public int Page { get; set; }
        public string RawText { get; set; } = string.Empty;
    }
}
