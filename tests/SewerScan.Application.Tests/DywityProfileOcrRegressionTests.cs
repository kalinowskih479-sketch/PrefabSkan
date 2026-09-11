using System.Linq;
using System.Threading.Tasks;
using SewerScan.Application.Models;
using SewerScan.Infrastructure.Parsers;
using Xunit;

namespace SewerScan.Application.Tests;

public class DywityProfileOcrRegressionTests
{
    [Fact]
    public async Task Storm_Profile_Uses_Observed_Kd_Family_For_Numeric_Node_Row()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] PROFIL KANALIZACJI DESZCZOWEJ RZĘDNE DNA PRZEWODU",
            ExtractionEngine = "OCR/Tesseract tiled"
        };

        // PT_S_4 OCR pattern: only one KD prefix survived, while the remaining
        // identifiers in the same engineering-table row were read as bare numbers.
        page.Items.Add(new TextItem { Text = "KDist", X = 35, Y = 500, Width = 34, Height = 10 });
        page.Items.Add(new TextItem { Text = "KD3", X = 335, Y = 500, Width = 24, Height = 10 });

        for (var number = 1; number <= 6; number++)
        {
            var x = 100 + (number - 1) * 115;
            page.Items.Add(new TextItem { Text = number.ToString(), X = x, Y = 500, Width = 10, Height = 10 });
            page.Items.Add(new TextItem { Text = $"{101 + number},20", X = x - 8, Y = 390, Width = 34, Height = 10 });
            page.Items.Add(new TextItem { Text = $"{100 + number},10", X = x - 8, Y = 350, Width = 34, Height = 10 });
            page.Items.Add(new TextItem { Text = "DN1200", X = x - 10, Y = 300, Width = 44, Height = 10 });
        }

        // Plausible-looking OCR garbage outside the actual node row.
        var garbage = new[] { "D5", "SO", "S8", "KS45", "S6", "S42", "S0" };
        for (var index = 0; index < garbage.Length; index++)
        {
            var x = 760 + index * 85;
            page.Items.Add(new TextItem { Text = garbage[index], X = x, Y = 820, Width = 28, Height = 10 });
            page.Items.Add(new TextItem { Text = $"{128 + index},20", X = x - 5, Y = 710, Width = 34, Height = 10 });
            page.Items.Add(new TextItem { Text = $"{127 + index},10", X = x - 5, Y = 670, Width = 34, Height = 10 });
            if (index < 5)
                page.Items.Add(new TextItem { Text = "DN1200", X = x - 8, Y = 630, Width = 44, Height = 10 });
        }

        var result = await parser.ParseAsync(new[] { page });
        var identifiers = result.Manholes.Select(m => m.Identifier).ToArray();

        Assert.Contains("KDIST", identifiers);
        Assert.All(Enumerable.Range(1, 6), number => Assert.Contains($"KD{number}", identifiers));
        Assert.DoesNotContain(identifiers, id => id == "D5" || id == "SO" || id == "S8" || id == "KS45");
        Assert.DoesNotContain(identifiers, id => id != null && (id.StartsWith("D") || id.StartsWith("S")));
    }

    [Fact]
    public async Task Sanitary_Profile_Prefers_Ks_When_Both_Kd_And_Ks_Appear_Credible()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] PROFIL KANALIZACJI SANITARNEJ RZĘDNE DNA PRZEWODU",
            ExtractionEngine = "OCR/Tesseract tiled"
        };

        page.Items.Add(new TextItem { Text = "KD2", X = 215, Y = 500, Width = 24, Height = 10 });
        page.Items.Add(new TextItem { Text = "KS3", X = 330, Y = 500, Width = 24, Height = 10 });
        for (var number = 1; number <= 6; number++)
        {
            var x = 100 + (number - 1) * 115;
            page.Items.Add(new TextItem { Text = number.ToString(), X = x, Y = 500, Width = 10, Height = 10 });
            page.Items.Add(new TextItem { Text = $"{101 + number},20", X = x - 8, Y = 390, Width = 34, Height = 10 });
            page.Items.Add(new TextItem { Text = $"{100 + number},10", X = x - 8, Y = 350, Width = 34, Height = 10 });
        }

        var result = await parser.ParseAsync(new[] { page });
        var identifiers = result.Manholes.Select(m => m.Identifier).ToArray();

        Assert.All(Enumerable.Range(1, 6), number => Assert.Contains($"KS{number}", identifiers));
        Assert.DoesNotContain(identifiers, id => id != null && id.StartsWith("KD"));
    }

    [Fact]
    public async Task Profile_Node_Row_With_Kdist_Wins_Over_Dense_Engineering_Like_Ocr_Garbage()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] PROFIL KANALIZACJI DESZCZOWEJ SO SO S8 S8",
            ExtractionEngine = "OCR/Tesseract tiled"
        };

        foreach (var item in new[]
        {
            ("KDist", 475d), ("KDi", 915d), ("KD2", 1232d), ("KD3", 1311d),
            ("KD4", 1524d), ("KDS", 1848d), ("KD6", 2119d)
        })
            page.Items.Add(new TextItem { Text = item.Item1, X = item.Item2, Y = 1133, Width = 24, Height = 10 });
        page.Items.Add(new TextItem { Text = "SO", X = 3424, Y = 1144, Width = 12, Height = 19 });

        var garbage = new[] { "D5", "SO", "S8", "KS45", "S6", "S42", "S0" };
        for (var index = 0; index < garbage.Length; index++)
        {
            var x = 1300 + index * 115;
            page.Items.Add(new TextItem { Text = garbage[index], X = x, Y = 700, Width = 24, Height = 10 });
            page.Items.Add(new TextItem { Text = $"{130 + index},20", X = x, Y = 850, Width = 34, Height = 10 });
            page.Items.Add(new TextItem { Text = $"{129 + index},10", X = x, Y = 900, Width = 34, Height = 10 });
            if (index < 3)
                page.Items.Add(new TextItem { Text = "DN1200", X = x, Y = 950, Width = 44, Height = 10 });
        }

        var result = await parser.ParseAsync(new[] { page });
        var identifiers = result.Manholes.Select(m => m.Identifier).ToArray();

        foreach (var expected in new[] { "KDIST", "KD1", "KD2", "KD3", "KD4", "KD5", "KD6" })
            Assert.Contains(expected, identifiers);
        Assert.DoesNotContain(identifiers, id => garbage.Contains(id));
    }

    [Fact]
    public async Task Ocr_Profile_Keeps_Only_Material_Backed_Standard_Pipe_Diameters()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "OCR/Tesseract tiled",
            Text = """
                [[PREFABSCAN_DRAWING:PROFIL]]
                PVC DN200
                PVC DN250
                PVC DN300
                PE D500
                DN600
                PE DN1200
                PVC DN2
                PVC d=128
                PE d=107
                PVC29
                PE054
                """
        };
        page.Items.Add(new TextItem { Text = "KD1", X = 100, Y = 500, Width = 24, Height = 10 });
        page.Items.Add(new TextItem { Text = "KD2", X = 300, Y = 500, Width = 24, Height = 10 });
        page.Items.Add(new TextItem { Text = "DN1200", X = 100, Y = 300, Width = 40, Height = 10 });
        page.Items.Add(new TextItem { Text = "DNi200", X = 300, Y = 300, Width = 40, Height = 10 });

        var result = await parser.ParseAsync(new[] { page });
        var pipes = result.Pipes.Select(p => (Material: p.Material?.ToUpperInvariant(), p.DiameterMm)).ToArray();

        Assert.Contains(("PVC", 200), pipes);
        Assert.Contains(("PVC", 250), pipes);
        Assert.Contains(("PVC", 300), pipes);
        Assert.Contains(("PE", 500), pipes);
        Assert.Equal(4, pipes.Length);
    }

    [Fact]
    public async Task Ocr_Profile_Preserves_Large_MaterialBacked_Pipe_Without_Manhole_Row_Evidence()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "OCR/Tesseract tiled",
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] PE DN1200"
        };

        var result = await parser.ParseAsync(new[] { page });

        Assert.Contains(result.Pipes, pipe => pipe.Material == "PE" && pipe.DiameterMm == 1200);
    }

    [Theory]
    [InlineData(280)]
    [InlineData(355)]
    [InlineData(560)]
    [InlineData(710)]
    public async Task Ocr_Profile_Preserves_Valid_Pe_Diameters_Outside_Common_Allowlist(int diameter)
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "OCR/Tesseract tiled",
            Text = $"[[PREFABSCAN_DRAWING:PROFIL]] PE DN{diameter}"
        };

        var result = await parser.ParseAsync(new[] { page });

        Assert.Contains(result.Pipes, pipe => pipe.Material == "PE" && pipe.DiameterMm == diameter);
    }

    [Fact]
    public async Task Repeated_Large_Dn_Away_From_Mh_Row_Remains_A_Pipe()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "OCR/Tesseract tiled",
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] PE DN1200"
        };
        page.Items.Add(new TextItem { Text = "DN1200", X = 100, Y = 100, Width = 40, Height = 10 });
        page.Items.Add(new TextItem { Text = "DN1200", X = 1000, Y = 800, Width = 40, Height = 10 });

        var result = await parser.ParseAsync(new[] { page });

        Assert.Contains(result.Pipes, pipe => pipe.Material == "PE" && pipe.DiameterMm == 1200);
    }

    [Fact]
    public async Task Ocr_Profile_Cleanup_Does_Not_Remove_Pipes_From_NonOcr_Pages()
    {
        var parser = new SewerProjectParser();
        var pages = new[]
        {
            new PageText
            {
                PageNumber = 1,
                ExtractionEngine = "PdfPig",
                Text = "[[PREFABSCAN_DRAWING:PROFIL]] PVC DN350"
            },
            new PageText
            {
                PageNumber = 2,
                ExtractionEngine = "OCR/Tesseract tiled",
                Text = "PVC DN2"
            }
        };

        var result = await parser.ParseAsync(pages);

        Assert.Contains(result.Pipes, pipe => pipe.Page == 1 && pipe.Material == "PVC" && pipe.DiameterMm == 350);
        Assert.DoesNotContain(result.Pipes, pipe => pipe.Page == 2);
    }

    [Fact]
    public async Task NonOcr_Pzt_Does_Not_Convert_Kds_To_Kd5()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "PdfPig",
            Text = "[[PREFABSCAN_DRAWING:PZT]]"
        };
        page.Items.Add(new TextItem { Text = "KDS", X = 100, Y = 100, Width = 24, Height = 10 });

        var result = await parser.ParseAsync(new[] { page });

        Assert.DoesNotContain(result.Manholes, manhole => manhole.Identifier == "KD5");
    }

    [Fact]
    public async Task Solitary_Kdist_Does_Not_Suppress_Repeated_Text_Identifier()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "OCR/Tesseract tiled",
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] KDist D7 D7"
        };
        page.Items.Add(new TextItem { Text = "KDist", X = 100, Y = 100, Width = 34, Height = 10 });

        var result = await parser.ParseAsync(new[] { page });

        Assert.Contains(result.Manholes, manhole => manhole.Identifier == "D7");
    }

    [Fact]
    public async Task Sanitary_Profile_Repairs_Decimal_Branch_Labels_Only_Inside_Credible_Node_Row()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "OCR/Tesseract tiled",
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] PROFIL KANALIZACJI SANITARNEJ SO SO"
        };

        foreach (var item in new[]
        {
            ("KSist", 463d), ("KS1", 502d), ("KS2", 885d), ("KS3", 1244d),
            ("KS2", 1327d), ("KS21", 1366d), ("KSe.1", 1366d),
            ("KS3", 1492d), ("s31", 1512d)
        })
            page.Items.Add(new TextItem { Text = item.Item1, X = item.Item2, Y = 1134, Width = 24, Height = 10 });

        page.Items.Add(new TextItem { Text = "SO", X = 2083, Y = 1040, Width = 12, Height = 27 });

        var result = await parser.ParseAsync(new[] { page });
        var identifiers = result.Manholes.Select(m => m.Identifier).ToArray();

        foreach (var expected in new[] { "KS1", "KS2", "KS2.1", "KS3", "KS3.1" })
            Assert.Contains(expected, identifiers);
        Assert.DoesNotContain("KS21", identifiers);
        Assert.DoesNotContain("S31", identifiers);
        Assert.DoesNotContain("SO", identifiers);
    }

    [Fact]
    public async Task Credible_Profile_Row_Still_Allows_Repeated_TextOnly_Node_From_The_Same_Family()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "OCR/Tesseract tiled",
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] KANALIZACJA DESZCZOWA KD4 KD4 SO SO"
        };
        foreach (var item in new[] { ("KD1", 100d), ("KD2", 250d), ("KD3", 400d) })
            page.Items.Add(new TextItem { Text = item.Item1, X = item.Item2, Y = 500, Width = 24, Height = 10 });

        var result = await parser.ParseAsync(new[] { page });
        var identifiers = result.Manholes.Select(m => m.Identifier).ToArray();

        Assert.Contains("KD4", identifiers);
        Assert.DoesNotContain("SO", identifiers);
    }

    [Fact]
    public async Task PdfPig_Profile_With_Strong_Kd_Row_Rejects_Text_Garbage_From_Other_Families()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "PdfPig/strict",
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] KANALIZACJA DESZCZOWA S2 S2 D8 D8"
        };
        for (var number = 1; number <= 10; number++)
            page.Items.Add(new TextItem { Text = $"KD{number}", X = number * 100, Y = 500, Width = 24, Height = 10 });

        var result = await parser.ParseAsync(new[] { page });

        Assert.All(Enumerable.Range(1, 10), number => Assert.Contains(result.Manholes, m => m.Identifier == $"KD{number}"));
        Assert.DoesNotContain(result.Manholes, m => m.Identifier == "S2" || m.Identifier == "D8");
    }

    [Fact]
    public async Task Profile_Parses_PvcU_Sdr_Schedule_Across_Lines_Without_Treating_D400_Cover_As_Pipe()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "PdfPig/strict",
            Text = """
                [[PREFABSCAN_DRAWING:PROFIL]]
                Studnia betonowa ø1200 z włazem żeliwnym klasy D400
                PVC-U_SDR34_l
                200×5,9
                PVC-U_SDR34_l
                250×7,3
                """
        };

        var result = await parser.ParseAsync(new[] { page });
        var pipes = result.Pipes.Select(p => (p.Material, p.DiameterMm)).ToArray();

        Assert.Contains(("PVC", 200), pipes);
        Assert.Contains(("PVC", 250), pipes);
        Assert.DoesNotContain(result.Pipes, p => p.DiameterMm == 400);
    }

    [Fact]
    public async Task PdfPig_Profile_Prefers_Cohesive_Kd_Row_Over_Denser_SingleLetter_Glyph_Garbage()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "PdfPig/strict",
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] KANALIZACJA DESZCZOWA"
        };
        foreach (var number in new[] { 1, 2, 4, 5, 6, 7, 8 })
            page.Items.Add(new TextItem { Text = $"KD{number}", X = number * 100, Y = 50, Width = 24, Height = 10 });
        for (var number = 0; number < 12; number++)
            page.Items.Add(new TextItem { Text = number % 2 == 0 ? $"S{number}" : $"D{number}", X = 100 + number * 80, Y = 130, Width = 20, Height = 10 });

        var result = await parser.ParseAsync(new[] { page });
        var identifiers = result.Manholes.Select(m => m.Identifier).ToArray();

        foreach (var number in new[] { 1, 2, 4, 5, 6, 7, 8 })
            Assert.Contains($"KD{number}", identifiers);
        Assert.DoesNotContain(identifiers, id => id != null && (id.StartsWith("S") || (id.StartsWith("D") && !id.StartsWith("KD"))));
    }

    [Fact]
    public async Task Profile_Recovers_Bare_PvcU_Diameter_When_Followed_By_Slope_Row()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "PdfPig/strict",
            Text = """
                [[PREFABSCAN_DRAWING:PROFIL]]
                PVC-U_SDR34_l
                200×5,9
                300
                1,5 %
                """
        };

        var result = await parser.ParseAsync(new[] { page });

        Assert.Contains(result.Pipes, pipe => pipe.Material == "PVC" && pipe.DiameterMm == 300);
    }

    [Fact]
    public async Task Profile_Does_Not_Assign_Distant_MixedMaterial_Bare_Diameter_To_PvcU()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "PdfPig/strict",
            Text = """
                [[PREFABSCAN_DRAWING:PROFIL]]
                PVC-U_SDR34_l
                200×5,9
                opis odcinka i dane techniczne oddzielające harmonogram
                betonowe typu WIPRO
                300
                1,5 %
                """
        };

        var result = await parser.ParseAsync(new[] { page });

        Assert.DoesNotContain(result.Pipes, pipe => pipe.Material == "PVC" && pipe.DiameterMm == 300);
    }

    [Fact]
    public async Task Ocr_Profile_Uses_Tight_Numeric_Node_Row_And_Rejects_Nearby_Garbage_Numbers()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "OCR/Tesseract tiled",
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] PROFIL KANALIZACJI DESZCZOWEJ KD8"
        };

        for (var number = 1; number <= 18; number++)
            page.Items.Add(new TextItem { Text = number.ToString(), X = 100 + number * 130, Y = 895, Width = 18, Height = 10 });
        page.Items.Add(new TextItem { Text = "KD8", X = 100 + 8 * 130, Y = 895, Width = 30, Height = 10 });
        page.Items.Add(new TextItem { Text = "KD1", X = 100 + 1 * 130, Y = 880, Width = 30, Height = 10 });

        foreach (var garbage in new[]
        {
            ("23", 750d, 880d), ("66", 1784d, 880d), ("68", 4657d, 880d),
            ("45", 5043d, 880d), ("21", 5607d, 880d)
        })
            page.Items.Add(new TextItem { Text = garbage.Item1, X = garbage.Item2, Y = garbage.Item3, Width = 18, Height = 10 });

        var result = await parser.ParseAsync(new[] { page });
        var identifiers = result.Manholes.Select(m => m.Identifier).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(Enumerable.Range(1, 18), number => Assert.Contains($"KD{number}", identifiers));
        foreach (var number in new[] { 21, 23, 45, 66, 68 })
            Assert.DoesNotContain($"KD{number}", identifiers);
    }

    [Fact]
    public async Task Profile_Parses_Concrete_Wipro_Pipe_Rows_But_Not_D400_Cover_Class()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "OCR/Tesseract tiled",
            Text = """
                [[PREFABSCAN_DRAWING:PROFIL]]
                studnia betonowa DN1000 z włazem klasy D400
                D 300 betonowe typu „WIPRO”
                D400
                betonowe typu WIPRO
                """
        };

        var result = await parser.ParseAsync(new[] { page });

        Assert.Contains(result.Pipes, pipe => pipe.Material == "BETON" && pipe.DiameterMm == 300);
        Assert.Contains(result.Pipes, pipe => pipe.Material == "BETON" && pipe.DiameterMm == 400);
        Assert.DoesNotContain(result.Pipes, pipe => pipe.DiameterMm == 1000);
    }

    [Fact]
    public async Task Profile_Parses_Pvc_When_Ocr_Puts_Dz_Diameter_Before_Material()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "OCR/Tesseract tiled",
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] Proj. średnica nominalna, materiał Dz200mm PVC lite SN8"
        };

        var result = await parser.ParseAsync(new[] { page });

        Assert.Contains(result.Pipes, pipe => pipe.Material == "PVC" && pipe.DiameterMm == 200);
    }

    [Fact]
    public async Task Composite_Profile_Does_Not_Invent_A_Network_From_Irregular_Numeric_Endpoints()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "OCR/Tesseract tiled",
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] PROFILE KANALIZACJI DESZCZOWEJ WYLOT ULICA KS2"
        };
        var endpoints = new[] { 2, 1, 5, 9, 16, 19, 11, 17, 3, 12, 4, 15, 13, 14, 6, 7, 18, 8 };
        for (var index = 0; index < endpoints.Length; index++)
            page.Items.Add(new TextItem { Text = endpoints[index].ToString(), X = 100 + index * 120, Y = 900, Width = 18, Height = 10 });
        page.Items.Add(new TextItem { Text = "KS2", X = 100, Y = 900, Width = 28, Height = 10 });

        var result = await parser.ParseAsync(new[] { page });

        Assert.True(result.Manholes.Count < 10);
    }

    [Fact]
    public async Task Profile_Accepts_K_Node_Family_And_Rejects_SingleLetter_Ocr_Garbage()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "OCR/Tesseract tiled",
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] WYLOT ULICA"
        };
        foreach (var number in Enumerable.Range(1, 5))
            page.Items.Add(new TextItem { Text = $"K{number}", X = number * 200, Y = 900, Width = 25, Height = 10 });
        page.Items.Add(new TextItem { Text = "S90", X = 450, Y = 900, Width = 25, Height = 10 });

        var result = await parser.ParseAsync(new[] { page });

        Assert.All(Enumerable.Range(1, 5), number => Assert.Contains(result.Manholes, m => m.Identifier == $"K{number}"));
        Assert.DoesNotContain(result.Manholes, m => m.Identifier == "S90");
    }
}
