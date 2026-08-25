using System.Collections.Generic;

namespace SewerScan.Application.Models
{
    /// <summary>
    /// Represents extracted text and items on a page.
    /// </summary>
    public sealed class PageText
    {
        public int PageNumber { get; set; }
        public string Text { get; set; } = string.Empty;
        /// <summary>Surowy tekst strony dokładnie w kolejności strumienia PDF.</summary>
        public string RawText { get; set; } = string.Empty;
        /// <summary>Tekst zrekonstruowany przez ekstraktor kolejności czytania.</summary>
        public string OrderedText { get; set; } = string.Empty;
        /// <summary>Diagnostyka ekstrakcji PDF dla tej strony.</summary>
        public string ExtractionDiagnostics { get; set; } = string.Empty;
        /// <summary>Silnik ekstrakcji użyty dla tej strony, np. PdfPig lub PDFium/Docnet.</summary>
        public string ExtractionEngine { get; set; } = string.Empty;
        public List<TextItem> Items { get; } = new();
    }

    public sealed class TextItem
    {
        public string Text { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
}
