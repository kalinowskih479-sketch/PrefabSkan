using System.Threading.Tasks;
using SewerScan.Application.Models;
using SewerScan.Infrastructure.Parsers;
using Xunit;

namespace SewerScan.Application.Tests;

public class OcrInlineManholeFieldsRegressionTests
{
    [Fact]
    public async Task Ocr_Profile_Recovers_Inline_Dn_Height_Type_And_Crown_When_Spatial_Window_Is_Fragmented()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] D7 DN1200 H=1.33 betonowa wlaz zeliwny",
            ExtractionEngine = "OCR/Tesseract tiled"
        };
        page.Items.Add(new TextItem { Text = "D7", X = 100, Y = 500, Width = 12, Height = 10 });
        page.Items.Add(new TextItem { Text = "DN1200", X = 900, Y = 500, Width = 40, Height = 10 });
        page.Items.Add(new TextItem { Text = "H=1.33", X = 1000, Y = 500, Width = 40, Height = 10 });
        page.Items.Add(new TextItem { Text = "betonowa", X = 1100, Y = 500, Width = 55, Height = 10 });
        page.Items.Add(new TextItem { Text = "wlaz", X = 1200, Y = 500, Width = 30, Height = 10 });
        page.Items.Add(new TextItem { Text = "zeliwny", X = 1250, Y = 500, Width = 45, Height = 10 });

        var result = await parser.ParseAsync(new[] { page });
        var d7 = Assert.Single(result.Manholes, m => m.Identifier == "D7");
        Assert.Equal(1200, d7.DiameterMm);
        Assert.Equal(1.33, d7.HeightM);
        Assert.Equal("betonowa", d7.Type);
        Assert.Contains("wlaz", d7.Crown ?? string.Empty, System.StringComparison.OrdinalIgnoreCase);
    }
}
