using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using SewerScan.Application.Interfaces;
using SewerScan.Application.Models;

namespace SewerScan.Infrastructure.Pdf
{
    /// <summary>
    /// Extracts text and word coordinates from PDF using PdfPig.
    /// </summary>
    public class PdfTextExtractor : ITextExtractor
    {
        public Task<IReadOnlyList<PageText>> ExtractAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));

            var pages = new List<PageText>();

            using var pdf = PdfDocument.Open(filePath);
            foreach (var page in pdf.GetPages())
            {
                var pt = new PageText { PageNumber = (int)page.Number };
                // full page text
                pt.Text = page.Text ?? string.Empty;

                // words with bounding boxes
                foreach (var word in page.GetWords())
                {
                    double x = 0, y = 0, w = 0, h = 0;
                    try
                    {
                        var wt = word.GetType();
                        var bboxProp = wt.GetProperty("BoundingBox") ?? wt.GetProperty("Bbox");
                        if (bboxProp != null)
                        {
                            var bbox = bboxProp.GetValue(word);
                            if (bbox != null)
                            {
                                // Try common property names
                                var leftProp = bbox.GetType().GetProperty("Left") ?? bbox.GetType().GetProperty("X1");
                                var topProp = bbox.GetType().GetProperty("Top") ?? bbox.GetType().GetProperty("Y1");
                                var rightProp = bbox.GetType().GetProperty("Right") ?? bbox.GetType().GetProperty("X2");
                                var bottomProp = bbox.GetType().GetProperty("Bottom") ?? bbox.GetType().GetProperty("Y2");
                                double left = leftProp != null ? Convert.ToDouble(leftProp.GetValue(bbox)) : 0;
                                double top = topProp != null ? Convert.ToDouble(topProp.GetValue(bbox)) : 0;
                                double right = rightProp != null ? Convert.ToDouble(rightProp.GetValue(bbox)) : left;
                                double bottom = bottomProp != null ? Convert.ToDouble(bottomProp.GetValue(bbox)) : top;
                                x = left;
                                y = top;
                                w = Math.Abs(right - left);
                                h = Math.Abs(top - bottom);
                            }
                        }
                    }
                    catch
                    {
                        // ignore and leave zeros
                    }

                    var item = new TextItem
                    {
                        Text = word.Text,
                        X = x,
                        Y = y,
                        Width = w,
                        Height = h
                    };
                    pt.Items.Add(item);
                }

                pages.Add(pt);
            }

            return Task.FromResult((IReadOnlyList<PageText>)pages);
        }
    }
}
