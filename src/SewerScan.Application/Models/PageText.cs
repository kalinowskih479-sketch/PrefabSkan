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
