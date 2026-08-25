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

    [Fact]
    public async Task Pzt_Recovers_Repeated_Local_Levels_When_Ocr_Geometry_Is_Displaced_From_Id()
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
            new TextItem { Text = "62,25", X = 575, Y = 480, Width = 30, Height = 10 },
            new TextItem { Text = "62,25", X = 577, Y = 478, Width = 30, Height = 10 },
            new TextItem { Text = "60,58", X = 578, Y = 455, Width = 30, Height = 10 },
            new TextItem { Text = "60,58", X = 580, Y = 453, Width = 30, Height = 10 },
            new TextItem { Text = "160", X = 545, Y = 520, Width = 18, Height = 10 },
            new TextItem { Text = "180", X = 550, Y = 535, Width = 18, Height = 10 }
        });

        var result = await parser.ParseAsync(new[] { page });
        var d6 = Assert.Single(result.Manholes.Where(m => m.Identifier == "D6"));

        Assert.Equal(62.25, d6.GroundElevationM);
        Assert.Equal(60.58, d6.InvertElevationM);
        Assert.Equal(1.67, d6.HeightM);
        Assert.Null(d6.DiameterMm);
    }

    [Fact]
    public async Task Stormwater_Profile_Prefers_D_Family_Band_Over_Higher_Scoring_S_Ocr_Band()
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
            // Real D-family node row. OCR support is incomplete, as in Batorego.
            new TextItem { Text = "D8", X = 100, Y = 500, Width = 14, Height = 10 },
            new TextItem { Text = "D9", X = 220, Y = 500, Width = 14, Height = 10 },
            new TextItem { Text = "D10", X = 340, Y = 500, Width = 18, Height = 10 },
            new TextItem { Text = "D11", X = 460, Y = 500, Width = 18, Height = 10 },
            new TextItem { Text = "D12", X = 580, Y = 500, Width = 18, Height = 10 },
            new TextItem { Text = "62,80", X = 92, Y = 320, Width = 28, Height = 10 },
            new TextItem { Text = "61,13", X = 92, Y = 270, Width = 28, Height = 10 },
            new TextItem { Text = "62,70", X = 572, Y = 320, Width = 28, Height = 10 },
            new TextItem { Text = "61,14", X = 572, Y = 270, Width = 28, Height = 10 },

            // OCR garbage band. It has more local numeric evidence, so the old score wins incorrectly.
            new TextItem { Text = "S4", X = 800, Y = 800, Width = 14, Height = 10 },
            new TextItem { Text = "S10", X = 920, Y = 800, Width = 18, Height = 10 },
            new TextItem { Text = "KD2", X = 1040, Y = 800, Width = 20, Height = 10 },
            new TextItem { Text = "64,20", X = 792, Y = 620, Width = 28, Height = 10 },
            new TextItem { Text = "62,20", X = 792, Y = 580, Width = 28, Height = 10 },
            new TextItem { Text = "64,10", X = 912, Y = 620, Width = 28, Height = 10 },
            new TextItem { Text = "62,10", X = 912, Y = 580, Width = 28, Height = 10 },
            new TextItem { Text = "DN1200", X = 790, Y = 550, Width = 40, Height = 10 },
            new TextItem { Text = "DN1200", X = 910, Y = 550, Width = 40, Height = 10 }
        });

        var result = await parser.ParseAsync(new[] { page });

        Assert.Contains(result.Manholes, m => m.Identifier == "D9");
        Assert.Contains(result.Manholes, m => m.Identifier == "D10");
        Assert.Contains(result.Manholes, m => m.Identifier == "D11");
        Assert.DoesNotContain(result.Manholes, m => m.Identifier == "S10");
    }
}
