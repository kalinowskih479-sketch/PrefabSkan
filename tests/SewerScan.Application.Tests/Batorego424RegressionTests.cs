using System.Linq;
using System.Threading.Tasks;
using SewerScan.Application.Models;
using SewerScan.Infrastructure.Parsers;
using Xunit;

namespace SewerScan.Application.Tests;

public class Batorego424RegressionTests
{
    [Fact]
    public async Task Pzt_Deduplicates_Repeated_Ocr_Levels_Before_Selecting_Elevation_Pair()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "[[PREFABSCAN_DRAWING:PZT]] D6",
            ExtractionEngine = "OCR/Tesseract tiled"
        };

        page.Items.AddRange(new[]
        {
            new TextItem { Text = "D6", X = 500, Y = 500, Width = 16, Height = 10 },

            // Batorego OCR pattern: the same level is emitted several times and is slightly
            // closer to the identifier than the second distinct level.
            new TextItem { Text = "62,25", X = 494, Y = 482, Width = 30, Height = 10 },
            new TextItem { Text = "62,25", X = 496, Y = 480, Width = 30, Height = 10 },
            new TextItem { Text = "62,25", X = 498, Y = 478, Width = 30, Height = 10 },
            new TextItem { Text = "62,25", X = 500, Y = 476, Width = 30, Height = 10 },

            new TextItem { Text = "60,58", X = 505, Y = 455, Width = 30, Height = 10 },
            new TextItem { Text = "60,58", X = 507, Y = 453, Width = 30, Height = 10 }
        });

        var result = await parser.ParseAsync(new[] { page });
        var d6 = Assert.Single(result.Manholes.Where(m => m.Identifier == "D6"));

        Assert.Equal(62.25, d6.GroundElevationM);
        Assert.Equal(60.58, d6.InvertElevationM);
        Assert.Equal(1.67, d6.HeightM);
    }
}
