using System.Linq;
using System.Threading.Tasks;
using SewerScan.Application.Models;
using SewerScan.Infrastructure.Parsers;
using Xunit;

namespace SewerScan.Application.Tests;

public class BatoregoRealOcrRegressionTests
{
    [Fact]
    public async Task Profile_Node_Row_Prefers_Engineering_Table_Alignment_Over_Denser_Ocr_Garbage_Band()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] PROFIL KANALIZACJI RZĘDNE DNA PRZEWODU",
            ExtractionEngine = "OCR/Tesseract tiled"
        };

        // Real Batorego failure pattern: OCR produced a denser band of plausible-looking
        // identifiers (S79, S13, S06/09, S61.16...) than the actual profile node row.
        page.Items.AddRange(new[]
        {
            // True node row — fewer IDs, but every column is supported by repeated engineering rows.
            new TextItem { Text = "D1", X = 100, Y = 500, Width = 12, Height = 10 },
            new TextItem { Text = "D2", X = 220, Y = 500, Width = 12, Height = 10 },
            new TextItem { Text = "D3", X = 340, Y = 500, Width = 12, Height = 10 },
            new TextItem { Text = "D4", X = 460, Y = 500, Width = 12, Height = 10 },

            // Ground row.
            new TextItem { Text = "62,70", X = 92,  Y = 320, Width = 28, Height = 10 },
            new TextItem { Text = "62,64", X = 212, Y = 320, Width = 28, Height = 10 },
            new TextItem { Text = "62,60", X = 332, Y = 320, Width = 28, Height = 10 },
            new TextItem { Text = "62,55", X = 452, Y = 320, Width = 28, Height = 10 },

            // Invert row.
            new TextItem { Text = "60,49", X = 92,  Y = 270, Width = 28, Height = 10 },
            new TextItem { Text = "60,43", X = 212, Y = 270, Width = 28, Height = 10 },
            new TextItem { Text = "61,04", X = 332, Y = 270, Width = 28, Height = 10 },
            new TextItem { Text = "61,51", X = 452, Y = 270, Width = 28, Height = 10 },

            // Depth row and structure DN row.
            new TextItem { Text = "2,21", X = 92,  Y = 245, Width = 24, Height = 10 },
            new TextItem { Text = "2,21", X = 212, Y = 245, Width = 24, Height = 10 },
            new TextItem { Text = "1,56", X = 332, Y = 245, Width = 24, Height = 10 },
            new TextItem { Text = "1,04", X = 452, Y = 245, Width = 24, Height = 10 },
            new TextItem { Text = "DN1200", X = 88,  Y = 215, Width = 40, Height = 10 },
            new TextItem { Text = "DN1200", X = 208, Y = 215, Width = 40, Height = 10 },
            new TextItem { Text = "DN1200", X = 328, Y = 215, Width = 40, Height = 10 },
            new TextItem { Text = "DN1200", X = 448, Y = 215, Width = 40, Height = 10 },

            // Denser OCR garbage band, intentionally without coherent table rows at these columns.
            new TextItem { Text = "S79", X = 700, Y = 900, Width = 16, Height = 10 },
            new TextItem { Text = "S13", X = 785, Y = 900, Width = 16, Height = 10 },
            new TextItem { Text = "S06/09", X = 875, Y = 900, Width = 28, Height = 10 },
            new TextItem { Text = "S61.16", X = 970, Y = 900, Width = 30, Height = 10 },
            new TextItem { Text = "S1", X = 1080, Y = 900, Width = 12, Height = 10 },
            new TextItem { Text = "S8", X = 1190, Y = 900, Width = 12, Height = 10 },
            new TextItem { Text = "S3", X = 1300, Y = 900, Width = 12, Height = 10 }
        });

        var result = await parser.ParseAsync(new[] { page });

        Assert.Contains(result.Manholes, m => m.Identifier == "D1");
        Assert.Contains(result.Manholes, m => m.Identifier == "D2");
        Assert.Contains(result.Manholes, m => m.Identifier == "D3");
        Assert.Contains(result.Manholes, m => m.Identifier == "D4");
        Assert.DoesNotContain(result.Manholes, m => m.Identifier == "S79" || m.Identifier == "S61.16" || m.Identifier == "S06/09");

        Assert.All(result.Manholes.Where(m => new[] { "D1", "D2", "D3", "D4" }.Contains(m.Identifier)),
            m => Assert.Equal(1200, m.DiameterMm));
    }
}
