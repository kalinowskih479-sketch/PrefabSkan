using System.Linq;
using System.Threading.Tasks;
using SewerScan.Application.Models;
using SewerScan.Infrastructure.Parsers;
using Xunit;

namespace SewerScan.Application.Tests;

public class SewerProjectParserTests
{
    [Fact]
    public async Task Parses_D_And_KD_Identifiers_Separately()
    {
        var parser = new SewerProjectParser();
        var pages = new[]
        {
            new PageText { PageNumber = 1, Text = "D1 studnia DN1200\nD2 studnia DN1200\nKD3 studnia DN1000" }
        };

        var result = await parser.ParseAsync(pages);

        Assert.Contains(result.Manholes, x => x.Identifier == "D1");
        Assert.Contains(result.Manholes, x => x.Identifier == "D2");
        Assert.Contains(result.Manholes, x => x.Identifier == "KD3");
    }

    [Fact]
    public async Task Does_Not_Merge_Different_Manholes_With_The_Same_Diameter()
    {
        var parser = new SewerProjectParser();
        var pages = new[]
        {
            new PageText { PageNumber = 1, Text = "D1 studnia DN1200\nD2 studnia DN1200" }
        };

        var result = await parser.ParseAsync(pages);

        Assert.Contains(result.Manholes, x => x.Identifier == "D1");
        Assert.Contains(result.Manholes, x => x.Identifier == "D2");
        Assert.NotSame(
            result.Manholes.Single(x => x.Identifier == "D1"),
            result.Manholes.Single(x => x.Identifier == "D2"));
    }

    [Fact]
    public async Task Parses_Manhole_Details_And_Transitions()
    {
        var parser = new SewerProjectParser();
        var pages = new[]
        {
            new PageText
            {
                PageNumber = 1,
                Text = "D5 Studnia kinetowa DN2000 wys. całk. 4,50 m zwieńczenie właz żeliwny PVC DN200 PVC DN200 PP DN300"
            }
        };

        var result = await parser.ParseAsync(pages);
        var manhole = Assert.Single(result.Manholes.Where(x => x.Identifier == "D5"));

        Assert.Equal(2000, manhole.DiameterMm);
        Assert.Equal(4.5, manhole.HeightM);
        Assert.Contains(manhole.Transitions, x => x.Material == "PVC" && x.DiameterMm == 200 && x.Quantity >= 2);
        Assert.Contains(manhole.Transitions, x => x.Material == "PP" && x.DiameterMm == 300);
    }
}

public class SewerProjectSpatialParserTests
{
    [Fact]
    public async Task Spatial_Mode_Keeps_Transitions_With_Their_Own_Manhole()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "D1 D2 studnia DN1200 PVC DN200 studnia DN1500 PP DN300"
        };

        page.Items.AddRange(new[]
        {
            new TextItem { Text = "D1", X = 100, Y = 500, Width = 12, Height = 10 },
            new TextItem { Text = "studnia", X = 80, Y = 470, Width = 35, Height = 10 },
            new TextItem { Text = "DN1200", X = 120, Y = 470, Width = 35, Height = 10 },
            new TextItem { Text = "PVC", X = 90, Y = 440, Width = 20, Height = 10 },
            new TextItem { Text = "DN200", X = 115, Y = 440, Width = 30, Height = 10 },

            new TextItem { Text = "D2", X = 500, Y = 500, Width = 12, Height = 10 },
            new TextItem { Text = "studnia", X = 480, Y = 470, Width = 35, Height = 10 },
            new TextItem { Text = "DN1500", X = 520, Y = 470, Width = 35, Height = 10 },
            new TextItem { Text = "PP", X = 490, Y = 440, Width = 15, Height = 10 },
            new TextItem { Text = "DN300", X = 510, Y = 440, Width = 30, Height = 10 }
        });

        var result = await parser.ParseAsync(new[] { page });

        var d1 = Assert.Single(result.Manholes.Where(x => x.Identifier == "D1"));
        var d2 = Assert.Single(result.Manholes.Where(x => x.Identifier == "D2"));

        Assert.Equal(1200, d1.DiameterMm);
        Assert.Contains(d1.Transitions, x => x.Material == "PVC" && x.DiameterMm == 200);
        Assert.DoesNotContain(d1.Transitions, x => x.Material == "PP" && x.DiameterMm == 300);

        Assert.Equal(1500, d2.DiameterMm);
        Assert.Contains(d2.Transitions, x => x.Material == "PP" && x.DiameterMm == 300);
        Assert.DoesNotContain(d2.Transitions, x => x.Material == "PVC" && x.DiameterMm == 200);
    }

    [Fact]
    public async Task Spatial_Mode_Does_Not_Treat_1013_As_Manhole_Diameter()
    {
        var parser = new SewerProjectParser();
        var page = new PageText { PageNumber = 1, Text = "D5 studnia DN2000 PE DN1013" };
        page.Items.AddRange(new[]
        {
            new TextItem { Text = "D5", X = 200, Y = 500, Width = 12, Height = 10 },
            new TextItem { Text = "studnia", X = 180, Y = 470, Width = 35, Height = 10 },
            new TextItem { Text = "DN2000", X = 220, Y = 470, Width = 35, Height = 10 },
            new TextItem { Text = "PE", X = 190, Y = 440, Width = 15, Height = 10 },
            new TextItem { Text = "DN1013", X = 210, Y = 440, Width = 35, Height = 10 }
        });

        var result = await parser.ParseAsync(new[] { page });
        var d5 = Assert.Single(result.Manholes.Where(x => x.Identifier == "D5"));
        Assert.Equal(2000, d5.DiameterMm);
    }
}

public class PrefabScan06RegressionTests
{
    [Fact]
    public async Task Profile_Recognises_Dotted_Identifiers_And_SO_But_Rejects_KsPipeLabels()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "Profile podłużne kanalizacji deszczowej RZĘDNE DNA PRZEWODU D1.1 Ø1200 ist. ks200 D1.2 Ø1200 ist. przył. ks160 SO Ø2000"
        };

        page.Items.AddRange(new[]
        {
            new TextItem { Text = "D1.1", X = 100, Y = 500, Width = 20, Height = 10 },
            new TextItem { Text = "Ø1200", X = 120, Y = 500, Width = 30, Height = 10 },
            new TextItem { Text = "134.12", X = 101, Y = 460, Width = 30, Height = 10 },
            new TextItem { Text = "132.61", X = 101, Y = 430, Width = 30, Height = 10 },
            new TextItem { Text = "ks200", X = 110, Y = 520, Width = 25, Height = 10 },
            new TextItem { Text = "D1.2", X = 250, Y = 500, Width = 20, Height = 10 },
            new TextItem { Text = "Ø1200", X = 270, Y = 500, Width = 30, Height = 10 },
            new TextItem { Text = "ks160", X = 260, Y = 520, Width = 25, Height = 10 },
            new TextItem { Text = "SO", X = 400, Y = 500, Width = 15, Height = 10 },
            new TextItem { Text = "Ø2000", X = 420, Y = 500, Width = 30, Height = 10 }
        });

        var result = await parser.ParseAsync(new[] { page });

        Assert.Equal("PROFIL", result.DrawingType);
        Assert.Contains(result.Manholes, x => x.Identifier == "D1.1" && x.DiameterMm == 1200);
        Assert.Contains(result.Manholes, x => x.Identifier == "D1.2" && x.DiameterMm == 1200);
        Assert.Contains(result.Manholes, x => x.Identifier == "SO" && x.DiameterMm == 2000);
        Assert.DoesNotContain(result.Manholes, x => x.Identifier == "KS200" || x.Identifier == "KS160");

        var d11 = result.Manholes.Single(x => x.Identifier == "D1.1");
        Assert.Equal(1.51, d11.HeightM);
    }

    [Fact]
    public async Task Pzt_Computes_Height_From_Two_Nearby_Elevations_Without_Guessing_Pipe_Diameter()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "Plan sytuacyjno - wysokościowy Projekt zagospodarowania terenu D6 133,70 128,94 Ø800"
        };

        page.Items.AddRange(new[]
        {
            new TextItem { Text = "D6", X = 500, Y = 500, Width = 16, Height = 10 },
            new TextItem { Text = "133,70", X = 495, Y = 470, Width = 32, Height = 10 },
            new TextItem { Text = "128,94", X = 515, Y = 470, Width = 32, Height = 10 },
            new TextItem { Text = "Ø800", X = 510, Y = 520, Width = 25, Height = 10 }
        });

        var result = await parser.ParseAsync(new[] { page });
        var d6 = Assert.Single(result.Manholes.Where(x => x.Identifier == "D6"));

        Assert.Equal("PZT", result.DrawingType);
        Assert.Equal(4.76, d6.HeightM);
        Assert.Null(d6.DiameterMm);
        Assert.Equal("średnia", d6.Confidence);
    }

    [Fact]
    public async Task Spatial_Mode_Recognises_Compact_Wp_Identifiers()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "Projekt zagospodarowania terenu WP1 WP2 WP25"
        };

        page.Items.AddRange(new[]
        {
            new TextItem { Text = "WP1", X = 100, Y = 100, Width = 15, Height = 10 },
            new TextItem { Text = "WP2", X = 200, Y = 100, Width = 15, Height = 10 },
            new TextItem { Text = "WP25", X = 300, Y = 100, Width = 20, Height = 10 }
        });

        var result = await parser.ParseAsync(new[] { page });

        Assert.Contains(result.Inlets, x => x.Identifier == "WP1");
        Assert.Contains(result.Inlets, x => x.Identifier == "WP2");
        Assert.Contains(result.Inlets, x => x.Identifier == "WP25");
    }
}

public class PrefabScan07RegressionTests
{
    [Fact]
    public async Task Pzt_Reconstructs_Split_D6Slash1_And_Keeps_Its_Own_Elevations()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "Plan sytuacyjno - wysokościowy D6/1 133,55 126,47 D7 133,65 126,51"
        };

        // Mirrors the CAD-export pattern from the Olsztyn PZT: D6/1 is split into "D" + "6/1".
        page.Items.AddRange(new[]
        {
            new TextItem { Text = "D", X = 885, Y = 1903, Width = 15, Height = 20 },
            new TextItem { Text = "6/1", X = 900, Y = 1901, Width = 20, Height = 14 },
            new TextItem { Text = "133,55", X = 860, Y = 1892, Width = 23, Height = 8 },
            new TextItem { Text = "126,47", X = 860, Y = 1901, Width = 23, Height = 8 },

            new TextItem { Text = "D7", X = 882, Y = 1910, Width = 10, Height = 8 },
            new TextItem { Text = "133,65", X = 848, Y = 1934, Width = 23, Height = 8 },
            new TextItem { Text = "126,51", X = 848, Y = 1943, Width = 23, Height = 8 }
        });

        var result = await parser.ParseAsync(new[] { page });

        var d61 = Assert.Single(result.Manholes.Where(x => x.Identifier == "D6/1"));
        var d7 = Assert.Single(result.Manholes.Where(x => x.Identifier == "D7"));

        Assert.Equal(133.55, d61.GroundElevationM);
        Assert.Equal(126.47, d61.InvertElevationM);
        Assert.Equal(7.08, d61.HeightM);

        Assert.Equal(133.65, d7.GroundElevationM);
        Assert.Equal(126.51, d7.InvertElevationM);
        Assert.Equal(7.14, d7.HeightM);
    }

    [Fact]
    public async Task Pzt_Reconstructs_Split_D16_Identifier()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "Projekt zagospodarowania terenu D16"
        };

        page.Items.AddRange(new[]
        {
            new TextItem { Text = "D", X = 100, Y = 100, Width = 10, Height = 12 },
            new TextItem { Text = "16", X = 110, Y = 100, Width = 12, Height = 12 }
        });

        var result = await parser.ParseAsync(new[] { page });
        Assert.Contains(result.Manholes, x => x.Identifier == "D16");
    }
}

public class PrefabScan08RegressionTests
{
    [Fact]
    public async Task Text_Fallback_Preserves_Wp_Prefix()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "Projekt zagospodarowania terenu WP1 WP2 WP25"
        };

        var result = await parser.ParseAsync(new[] { page });

        Assert.Contains(result.Inlets, x => x.Identifier == "WP1");
        Assert.Contains(result.Inlets, x => x.Identifier == "WP2");
        Assert.Contains(result.Inlets, x => x.Identifier == "WP25");
        Assert.DoesNotContain(result.Inlets, x => x.Identifier == "1" || x.Identifier == "2" || x.Identifier == "25");
    }

    [Fact]
    public async Task Item_Stream_Recovers_Split_D20_D21_D22()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "Projekt zagospodarowania terenu"
        };

        page.Items.AddRange(new[]
        {
            new TextItem { Text = "D", X = 100, Y = 100, Width = 8, Height = 10 },
            new TextItem { Text = "20", X = 109, Y = 100, Width = 12, Height = 10 },
            new TextItem { Text = "D", X = 160, Y = 100, Width = 8, Height = 10 },
            new TextItem { Text = "21", X = 169, Y = 100, Width = 12, Height = 10 },
            new TextItem { Text = "D", X = 220, Y = 100, Width = 8, Height = 10 },
            new TextItem { Text = "22", X = 229, Y = 100, Width = 12, Height = 10 }
        });

        var result = await parser.ParseAsync(new[] { page });
        Assert.Contains(result.Manholes, x => x.Identifier == "D20");
        Assert.Contains(result.Manholes, x => x.Identifier == "D21");
        Assert.Contains(result.Manholes, x => x.Identifier == "D22");
    }
}


public class PrefabScan09RegressionTests
{
    [Fact]
    public async Task Pzt_Normalizes_Duplicated_Cad_Glyphs_For_D6Slash1_And_D7()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "Projekt zagospodarowania terenu"
        };

        page.Items.AddRange(new[]
        {
            new TextItem { Text = "DD", X = 885, Y = 1903, Width = 15, Height = 20 },
            new TextItem { Text = "66//11", X = 900, Y = 1901, Width = 20, Height = 14 },
            new TextItem { Text = "133,55", X = 860, Y = 1892, Width = 23, Height = 8 },
            new TextItem { Text = "1", X = 860, Y = 1901, Width = 4, Height = 8 },
            new TextItem { Text = "2", X = 864, Y = 1901, Width = 4, Height = 8 },
            new TextItem { Text = "6,47", X = 868, Y = 1901, Width = 15, Height = 8 },

            new TextItem { Text = "DD", X = 873, Y = 1944, Width = 15, Height = 20 },
            new TextItem { Text = "77", X = 888, Y = 1943, Width = 8, Height = 14 },
            new TextItem { Text = "133,65", X = 848, Y = 1934, Width = 23, Height = 8 },
            new TextItem { Text = "126,51", X = 848, Y = 1943, Width = 23, Height = 8 }
        });

        var result = await parser.ParseAsync(new[] { page });

        var d61 = Assert.Single(result.Manholes.Where(x => x.Identifier == "D6/1"));
        var d7 = Assert.Single(result.Manholes.Where(x => x.Identifier == "D7"));
        Assert.Equal(7.08, d61.HeightM);
        Assert.Equal(7.14, d7.HeightM);
    }
}

public class PrefabScan20OcrRegressionTests
{
    [Fact]
    public async Task Ocr_Pzt_Accepts_TwoDigit_Elevations_And_Computes_Height()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "OCR/Tesseract tiled",
            Text = "D 7 62,13 60,65 D 8 62,13 60,73"
        };
        page.Items.AddRange(new[]
        {
            new TextItem { Text = "D", X = 100, Y = 100, Width = 12, Height = 18 },
            new TextItem { Text = "7", X = 114, Y = 100, Width = 9, Height = 18 },
            new TextItem { Text = "62,13", X = 92, Y = 126, Width = 44, Height = 16 },
            new TextItem { Text = "60,65", X = 92, Y = 146, Width = 44, Height = 16 },
            new TextItem { Text = "D8", X = 300, Y = 100, Width = 24, Height = 18 },
            new TextItem { Text = "62,13", X = 292, Y = 126, Width = 44, Height = 16 },
            new TextItem { Text = "60,73", X = 292, Y = 146, Width = 44, Height = 16 }
        });

        var result = await parser.ParseAsync(new[] { page });
        var d7 = Assert.Single(result.Manholes.Where(x => x.Identifier == "D7"));
        var d8 = Assert.Single(result.Manholes.Where(x => x.Identifier == "D8"));
        Assert.Equal(62.13, d7.GroundElevationM);
        Assert.Equal(60.65, d7.InvertElevationM);
        Assert.Equal(1.48, d7.HeightM);
        Assert.Equal(1.40, d8.HeightM);
    }

    [Fact]
    public async Task Ocr_Repairs_ClosingBracket_In_Elevation()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "OCR/Tesseract tiled",
            Text = "D2 62,77 60,4]"
        };
        page.Items.AddRange(new[]
        {
            new TextItem { Text = "D2", X = 100, Y = 100, Width = 24, Height = 18 },
            new TextItem { Text = "62,77", X = 90, Y = 126, Width = 44, Height = 16 },
            new TextItem { Text = "60,4]", X = 90, Y = 146, Width = 44, Height = 16 }
        });

        var result = await parser.ParseAsync(new[] { page });
        var d2 = Assert.Single(result.Manholes.Where(x => x.Identifier == "D2"));
        Assert.Equal(60.41, d2.InvertElevationM);
        Assert.Equal(2.36, d2.HeightM);
    }

    [Fact]
    public async Task Ocr_Does_Not_Create_TextOnly_D60_Noise_Without_Elevations()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            ExtractionEngine = "OCR/Tesseract tiled",
            Text = "D60 mat= D00 przypadkowe fragmenty"
        };
        page.Items.Add(new TextItem { Text = "opis", X = 10, Y = 10, Width = 20, Height = 10 });

        var result = await parser.ParseAsync(new[] { page });
        Assert.DoesNotContain(result.Manholes, x => x.Identifier == "D60" || x.Identifier == "D00");
    }

    [Fact]
    public async Task Compact_Pvc160_Is_Recognised_As_Transition_In_Local_Context()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "PROFIL KANALIZACJI SANITARNEJ KS3 studnia DN1200 PVC160 PVC200"
        };
        page.Items.AddRange(new[]
        {
            new TextItem { Text = "KS3", X = 100, Y = 100, Width = 25, Height = 12 },
            new TextItem { Text = "studnia", X = 100, Y = 120, Width = 40, Height = 12 },
            new TextItem { Text = "DN1200", X = 145, Y = 120, Width = 45, Height = 12 },
            new TextItem { Text = "PVC160", X = 100, Y = 140, Width = 45, Height = 12 },
            new TextItem { Text = "PVC200", X = 150, Y = 140, Width = 45, Height = 12 }
        });
        var result = await parser.ParseAsync(new[] { page });
        var ks3 = Assert.Single(result.Manholes.Where(x => x.Identifier == "KS3"));
        Assert.Contains(ks3.Transitions, x => x.Material == "PVC" && x.DiameterMm == 160);
        Assert.Contains(ks3.Transitions, x => x.Material == "PVC" && x.DiameterMm == 200);
    }
}

public class PrefabScan21RegressionTests
{
    [Fact]
    public async Task Ocr_Normalizes_Leading_Zero_Identifiers_And_Rejects_Unsupported_High_Noise()
    {
        var parser = new SewerProjectParser();
        var page = new PageText { PageNumber = 1, Text = "Plan sytuacyjno wysokościowy S08 S95", ExtractionEngine = "OCR/Tesseract tiled" };
        page.Items.AddRange(new[]
        {
            new TextItem { Text = "S08", X = 100, Y = 100, Width = 20, Height = 10 },
            new TextItem { Text = "62,85", X = 105, Y = 120, Width = 30, Height = 10 },
            new TextItem { Text = "61,79", X = 105, Y = 138, Width = 30, Height = 10 },
            new TextItem { Text = "S95", X = 500, Y = 500, Width = 20, Height = 10 }
        });

        var result = await parser.ParseAsync(new[] { page });
        Assert.Contains(result.Manholes, x => x.Identifier == "S8");
        Assert.DoesNotContain(result.Manholes, x => x.Identifier == "S95");
    }

    [Fact]
    public async Task Ocr_Spatial_Context_Recovers_Split_Pipe_Transition()
    {
        var parser = new SewerProjectParser();
        var page = new PageText { PageNumber = 1, Text = "PROFIL KANALIZACJI D7 PVC 200", ExtractionEngine = "OCR/Tesseract tiled" };
        page.Items.AddRange(new[]
        {
            new TextItem { Text = "D7", X = 100, Y = 100, Width = 20, Height = 10 },
            new TextItem { Text = "62,13", X = 100, Y = 125, Width = 35, Height = 10 },
            new TextItem { Text = "60,65", X = 100, Y = 145, Width = 35, Height = 10 },
            new TextItem { Text = "PVC", X = 120, Y = 165, Width = 25, Height = 10 },
            new TextItem { Text = "200", X = 150, Y = 165, Width = 22, Height = 10 }
        });

        var result = await parser.ParseAsync(new[] { page });
        var d7 = Assert.Single(result.Manholes.Where(x => x.Identifier == "D7"));
        Assert.Equal(1.48, d7.HeightM);
        Assert.Contains(d7.Transitions, x => x.Material == "PVC" && x.DiameterMm == 200);
    }
}


public class PrefabScan32VisionHardeningTests
{
    [Fact]
    public async Task Ocr_High_Number_Without_Engineering_Evidence_Is_Marked_For_Verification()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "S95",
            RawText = "S95",
            OrderedText = "S95",
            ExtractionEngine = "OCR/Tesseract"
        };
        page.Items.Add(new TextItem { Text = "S95", X = 100, Y = 100, Width = 20, Height = 10 });

        var result = await parser.ParseAsync(new[] { page });
        var candidate = Assert.Single(result.Manholes.Where(x => x.Identifier == "S95"));
        Assert.Equal("niska", candidate.Confidence);
        Assert.Contains("weryfikacji", candidate.ValidationIssues, StringComparison.OrdinalIgnoreCase);
    }
}


public class PrefabScan37ArchitectureTests
{
    [Fact]
    public async Task Explicit_Pzt_Hint_Wins_Over_Profile_Words()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "[[PREFABSCAN_DRAWING:PZT]] PROFIL kanalizacji RZĘDNE DNA PRZEWODU"
        };
        page.Items.Add(new TextItem { Text = "D1", X = 100, Y = 500, Width = 12, Height = 10 });
        page.Items.Add(new TextItem { Text = "62,80", X = 130, Y = 500, Width = 25, Height = 10 });
        page.Items.Add(new TextItem { Text = "61,20", X = 130, Y = 480, Width = 25, Height = 10 });

        var result = await parser.ParseAsync(new[] { page });

        Assert.Equal("PZT", result.DrawingType);
        Assert.Contains(result.Manholes, m => m.Identifier == "D1");
    }

    [Fact]
    public async Task Profile_Row_Bands_Assign_Elevations_By_Structure_Column()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] RZĘDNE DNA PRZEWODU"
        };

        // Three structure columns.
        page.Items.AddRange(new[]
        {
            new TextItem { Text = "D1", X = 95,  Y = 500, Width = 10, Height = 10 },
            new TextItem { Text = "D2", X = 195, Y = 500, Width = 10, Height = 10 },
            new TextItem { Text = "D3", X = 295, Y = 500, Width = 10, Height = 10 },

            // Ground row.
            new TextItem { Text = "64,20", X = 90,  Y = 320, Width = 28, Height = 10 },
            new TextItem { Text = "64,10", X = 190, Y = 320, Width = 28, Height = 10 },
            new TextItem { Text = "64,00", X = 290, Y = 320, Width = 28, Height = 10 },

            // Invert row.
            new TextItem { Text = "62,20", X = 90,  Y = 270, Width = 28, Height = 10 },
            new TextItem { Text = "62,60", X = 190, Y = 270, Width = 28, Height = 10 },
            new TextItem { Text = "62,80", X = 290, Y = 270, Width = 28, Height = 10 },

            // Distractor values closer to the labels but not forming a repeated table band.
            new TextItem { Text = "63,90", X = 96, Y = 455, Width = 28, Height = 10 },
            new TextItem { Text = "60,00", X = 202, Y = 450, Width = 28, Height = 10 }
        });

        var result = await parser.ParseAsync(new[] { page });

        var d1 = Assert.Single(result.Manholes.Where(m => m.Identifier == "D1"));
        var d2 = Assert.Single(result.Manholes.Where(m => m.Identifier == "D2"));
        var d3 = Assert.Single(result.Manholes.Where(m => m.Identifier == "D3"));

        Assert.Equal(2.00, d1.HeightM);
        Assert.Equal(1.50, d2.HeightM);
        Assert.Equal(1.20, d3.HeightM);
        Assert.Equal(64.20, d1.GroundElevationM);
        Assert.Equal(62.20, d1.InvertElevationM);
    }

    [Fact]
    public async Task Profile_Repeated_Dn_Row_Can_Assign_Standard_Manhole_Diameter()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] PROFIL"
        };

        page.Items.AddRange(new[]
        {
            new TextItem { Text = "D1", X = 95,  Y = 500, Width = 10, Height = 10 },
            new TextItem { Text = "D2", X = 195, Y = 500, Width = 10, Height = 10 },
            new TextItem { Text = "D3", X = 295, Y = 500, Width = 10, Height = 10 },
            new TextItem { Text = "DN1200", X = 88,  Y = 220, Width = 35, Height = 10 },
            new TextItem { Text = "DN1200", X = 188, Y = 220, Width = 35, Height = 10 },
            new TextItem { Text = "DN1200", X = 288, Y = 220, Width = 35, Height = 10 },
            new TextItem { Text = "PVC160", X = 100, Y = 460, Width = 35, Height = 10 },
            new TextItem { Text = "PVC200", X = 200, Y = 460, Width = 35, Height = 10 }
        });

        var result = await parser.ParseAsync(new[] { page });

        Assert.All(result.Manholes.Where(m => new[] { "D1", "D2", "D3" }.Contains(m.Identifier)),
            m => Assert.Equal(1200, m.DiameterMm));
    }
}


public class PrefabScan39GeometryFirstTests
{
    [Fact]
    public async Task Geometry_First_Prefers_Repeated_Profile_Rows_Over_Nearby_Distractors()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] PROFIL KANALIZACJI RZĘDNE DNA PRZEWODU"
        };

        page.Items.AddRange(new[]
        {
            new TextItem { Text = "D1", X = 100, Y = 520, Width = 12, Height = 10 },
            new TextItem { Text = "D2", X = 220, Y = 520, Width = 12, Height = 10 },
            new TextItem { Text = "D3", X = 340, Y = 520, Width = 12, Height = 10 },
            new TextItem { Text = "D4", X = 460, Y = 520, Width = 12, Height = 10 },

            // True ground row.
            new TextItem { Text = "62,70", X = 92,  Y = 320, Width = 28, Height = 10 },
            new TextItem { Text = "62,64", X = 212, Y = 320, Width = 28, Height = 10 },
            new TextItem { Text = "62,60", X = 332, Y = 320, Width = 28, Height = 10 },
            new TextItem { Text = "62,55", X = 452, Y = 320, Width = 28, Height = 10 },

            // True invert row.
            new TextItem { Text = "60,49", X = 92,  Y = 270, Width = 28, Height = 10 },
            new TextItem { Text = "60,43", X = 212, Y = 270, Width = 28, Height = 10 },
            new TextItem { Text = "61,04", X = 332, Y = 270, Width = 28, Height = 10 },
            new TextItem { Text = "61,51", X = 452, Y = 270, Width = 28, Height = 10 },

            // Nearby OCR distractors which old nearest-number logic could steal.
            new TextItem { Text = "61,13", X = 103, Y = 487, Width = 28, Height = 10 },
            new TextItem { Text = "60,95", X = 223, Y = 482, Width = 28, Height = 10 },
            new TextItem { Text = "61,79", X = 343, Y = 478, Width = 28, Height = 10 },

            // True repeated DN row and pipe-size distractors.
            new TextItem { Text = "DN1200", X = 88,  Y = 215, Width = 40, Height = 10 },
            new TextItem { Text = "DN1200", X = 208, Y = 215, Width = 40, Height = 10 },
            new TextItem { Text = "DN1200", X = 328, Y = 215, Width = 40, Height = 10 },
            new TextItem { Text = "DN1200", X = 448, Y = 215, Width = 40, Height = 10 },
            new TextItem { Text = "PVC160", X = 100, Y = 450, Width = 35, Height = 10 },
            new TextItem { Text = "PVC200", X = 220, Y = 450, Width = 35, Height = 10 }
        });

        var result = await parser.ParseAsync(new[] { page });

        var d1 = Assert.Single(result.Manholes.Where(m => m.Identifier == "D1"));
        var d2 = Assert.Single(result.Manholes.Where(m => m.Identifier == "D2"));
        var d3 = Assert.Single(result.Manholes.Where(m => m.Identifier == "D3"));
        var d4 = Assert.Single(result.Manholes.Where(m => m.Identifier == "D4"));

        Assert.Equal(2.21, d1.HeightM);
        Assert.Equal(2.21, d2.HeightM);
        Assert.Equal(1.56, d3.HeightM);
        Assert.Equal(1.04, d4.HeightM);
        Assert.All(new[] { d1, d2, d3, d4 }, m => Assert.Equal(1200, m.DiameterMm));
    }

    [Fact]
    public async Task Geometry_First_Does_Not_Promote_Pipe_Dn_When_Not_Repeated_As_Structure_Row()
    {
        var parser = new SewerProjectParser();
        var page = new PageText
        {
            PageNumber = 1,
            Text = "[[PREFABSCAN_DRAWING:PROFIL]] PROFIL"
        };

        page.Items.AddRange(new[]
        {
            new TextItem { Text = "D1", X = 100, Y = 500, Width = 12, Height = 10 },
            new TextItem { Text = "D2", X = 220, Y = 500, Width = 12, Height = 10 },
            new TextItem { Text = "D3", X = 340, Y = 500, Width = 12, Height = 10 },
            new TextItem { Text = "PVC160", X = 95, Y = 440, Width = 35, Height = 10 },
            new TextItem { Text = "PVC200", X = 215, Y = 430, Width = 35, Height = 10 },
            new TextItem { Text = "PP300", X = 335, Y = 420, Width = 35, Height = 10 }
        });

        var result = await parser.ParseAsync(new[] { page });

        Assert.All(result.Manholes.Where(m => m.Identifier?.StartsWith("D") == true),
            m => Assert.Null(m.DiameterMm));
    }
}
