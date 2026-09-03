using System.Linq;
using System.Threading.Tasks;
using SewerScan.Application.Models;
using SewerScan.Infrastructure.Parsers;
using Xunit;

namespace SewerScan.Application.Tests;

public class Batorego425RegressionTests
{
    [Fact]
    public async Task Storm_Profile_Recovers_D_Identifiers_When_Ocr_Drops_Prefixes_In_Node_Row()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] PROFIL KANALIZACJI DESZCZOWEJ RZĘDNE DNA PRZEWODU",
            ExtractionEngine = "OCR/Tesseract tiled"
        };

        page.Items.AddRange(new[]
        {
            new TextItem { Text = "8", X = 100, Y = 500, Width = 10, Height = 10 },
            new TextItem { Text = "9", X = 220, Y = 500, Width = 10, Height = 10 },
            new TextItem { Text = "10", X = 340, Y = 500, Width = 14, Height = 10 },
            new TextItem { Text = "11", X = 460, Y = 500, Width = 14, Height = 10 },
            new TextItem { Text = "12", X = 580, Y = 500, Width = 14, Height = 10 },
            new TextItem { Text = "62,13", X = 92, Y = 320, Width = 30, Height = 10 },
            new TextItem { Text = "60,73", X = 92, Y = 270, Width = 30, Height = 10 },
            new TextItem { Text = "62,20", X = 212, Y = 320, Width = 30, Height = 10 },
            new TextItem { Text = "60,80", X = 212, Y = 270, Width = 30, Height = 10 },
            new TextItem { Text = "62,30", X = 332, Y = 320, Width = 30, Height = 10 },
            new TextItem { Text = "60,90", X = 332, Y = 270, Width = 30, Height = 10 },
            new TextItem { Text = "62,50", X = 452, Y = 320, Width = 30, Height = 10 },
            new TextItem { Text = "61,00", X = 452, Y = 270, Width = 30, Height = 10 },
            new TextItem { Text = "62,80", X = 572, Y = 320, Width = 30, Height = 10 },
            new TextItem { Text = "61,13", X = 572, Y = 270, Width = 30, Height = 10 },
            new TextItem { Text = "S10", X = 900, Y = 800, Width = 20, Height = 10 },
            new TextItem { Text = "KD2", X = 1030, Y = 800, Width = 22, Height = 10 },
            new TextItem { Text = "DN1200", X = 890, Y = 650, Width = 45, Height = 10 },
            new TextItem { Text = "DN1200", X = 1020, Y = 650, Width = 45, Height = 10 }
        });

        var result = await parser.ParseAsync(new[] { page });
        var ids = string.Join(",", result.Manholes.Select(m => m.Identifier));
        foreach (var expected in new[] { "D8", "D9", "D10", "D11", "D12" })
            Assert.True(result.Manholes.Any(m => m.Identifier == expected), $"Missing {expected}; IDs={ids}");
    }
}
