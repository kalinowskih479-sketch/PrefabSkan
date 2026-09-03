using System.Linq;
using System.Threading.Tasks;
using SewerScan.Application.Models;
using SewerScan.Infrastructure.Parsers;
using Xunit;

namespace SewerScan.Application.Tests;

public class PrefabScan426RegressionTests
{
    [Fact]
    public async Task Deszczowy_Profile_Recovers_D8_To_D12_From_Long_Sequential_Numeric_Row_Without_Elevation_Support()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "PROFIL KANALIZACJI DESZCZOWEJ PVC400 PVC200"
        };

        // Mirrors the Batorego failure: OCR loses the D prefix on the table row,
        // but still sees a long sequential run of node numbers. Elevation text is too
        // noisy/far away to satisfy the 4.2.5 engineering-column gate.
        page.Items.AddRange(new[]
        {
            new TextItem { Text = "8",  X = 500,  Y = 800, Width = 12, Height = 10 },
            new TextItem { Text = "9",  X = 650,  Y = 802, Width = 12, Height = 10 },
            new TextItem { Text = "10", X = 800,  Y = 799, Width = 18, Height = 10 },
            new TextItem { Text = "11", X = 950,  Y = 801, Width = 18, Height = 10 },
            new TextItem { Text = "12", X = 1100, Y = 800, Width = 18, Height = 10 },

            // Misread labels seen on the real profile must not block reconstruction.
            new TextItem { Text = "S10", X = 820, Y = 420, Width = 24, Height = 10 },
            new TextItem { Text = "KD2", X = 980, Y = 430, Width = 24, Height = 10 },
            new TextItem { Text = "1200", X = 820, Y = 500, Width = 30, Height = 10 },
            new TextItem { Text = "1200", X = 980, Y = 500, Width = 30, Height = 10 }
        });

        var result = await parser.ParseAsync(new[] { page });

        Assert.Equal("PROFIL", result.DrawingType);
        foreach (var id in new[] { "D8", "D9", "D10", "D11", "D12" })
            Assert.Contains(result.Manholes, x => x.Identifier == id);
    }
}
