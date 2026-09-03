using SewerScan.Application.DTO;
using SewerScan.Application.Services;
using Xunit;

namespace SewerScan.Application.Tests;

public class ProjectMergerTests
{
    [Fact]
    public void Merge_ComplementsPztWithProfileData_ByIdentifier()
    {
        var pzt = new ParsedProject { DrawingType = "PZT", SourceFile = "pzt.pdf" };
        pzt.SourceDocuments.Add("pzt.pdf");
        var pztManhole = new ParsedManhole
        {
            Identifier = "D6/1",
            GroundElevationM = 134.15,
            InvertElevationM = 126.47,
            HeightM = 7.08,
            Confidence = "średnia",
            SourceDocument = "pzt.pdf"
        };
        pztManhole.SourceDocuments.Add("pzt.pdf");
        pzt.Manholes.Add(pztManhole);

        var profile = new ParsedProject { DrawingType = "PROFIL", SourceFile = "profil.pdf" };
        profile.SourceDocuments.Add("profil.pdf");
        var profileManhole = new ParsedManhole
        {
            Identifier = "D6/1",
            DiameterMm = 1200,
            Type = "kinetowa",
            Crown = "właz żeliwny D400",
            Confidence = "średnia",
            SourceDocument = "profil.pdf"
        };
        profileManhole.SourceDocuments.Add("profil.pdf");
        profileManhole.Transitions.Add(new ManholeTransition { Material = "PP", DiameterMm = 300, Quantity = 2 });
        profile.Manholes.Add(profileManhole);

        var merged = ProjectMerger.Merge(new[] { pzt, profile });
        var actual = Assert.Single(merged.Manholes);

        Assert.Equal("D6/1", actual.Identifier);
        Assert.Equal(1200, actual.DiameterMm);
        Assert.Equal(134.15, actual.GroundElevationM);
        Assert.Equal(126.47, actual.InvertElevationM);
        Assert.Equal(7.08, actual.HeightM);
        Assert.Equal("kinetowa", actual.Type);
        Assert.Equal("właz żeliwny D400", actual.Crown);
        Assert.Equal(2, actual.SourceDocuments.Count);
        Assert.True(actual.CompletenessPercent >= 90);
        Assert.Equal("wysoka", actual.Confidence);
    }

    [Fact]
    public void Merge_DoesNotDoubleTransitionQuantityAcrossDrawings()
    {
        var a = new ParsedProject { DrawingType = "PZT", SourceFile = "a.pdf" };
        var b = new ParsedProject { DrawingType = "PROFIL", SourceFile = "b.pdf" };

        var ma = new ParsedManhole { Identifier = "D7", SourceDocument = "a.pdf" };
        ma.Transitions.Add(new ManholeTransition { Material = "PP", DiameterMm = 300, Quantity = 2 });
        var mb = new ParsedManhole { Identifier = "D7", SourceDocument = "b.pdf" };
        mb.Transitions.Add(new ManholeTransition { Material = "PP", DiameterMm = 300, Quantity = 2 });
        a.Manholes.Add(ma);
        b.Manholes.Add(mb);

        var merged = ProjectMerger.Merge(new[] { a, b });
        var transition = Assert.Single(Assert.Single(merged.Manholes).Transitions);
        Assert.Equal(2, transition.Quantity);
    }

    [Fact]
    public void Merge_NormalizesWpPrefix()
    {
        var p = new ParsedProject { SourceFile = "pzt.pdf" };
        p.Inlets.Add(new ParsedInlet { Identifier = "12", SourceDocument = "pzt.pdf" });
        p.Inlets.Add(new ParsedInlet { Identifier = "WP12", SourceDocument = "profil.pdf" });

        var merged = ProjectMerger.Merge(new[] { p });
        var inlet = Assert.Single(merged.Inlets);
        Assert.Equal("WP12", inlet.Identifier);
    }
    [Fact]
    public void Merge_FlagsConflictingElevations_InsteadOfSilentlyAveraging()
    {
        var a = new ParsedProject { SourceFile = "pzt.pdf" };
        var b = new ParsedProject { SourceFile = "profil.pdf" };
        a.Manholes.Add(new ParsedManhole { Identifier = "D7", GroundElevationM = 134.15, InvertElevationM = 126.51, SourceDocument = "pzt.pdf", Confidence = "średnia" });
        b.Manholes.Add(new ParsedManhole { Identifier = "D7", GroundElevationM = 134.15, InvertElevationM = 126.47, SourceDocument = "profil.pdf", Confidence = "średnia" });

        var merged = ProjectMerger.Merge(new[] { a, b });
        var actual = Assert.Single(merged.Manholes);

        Assert.Contains("sprzeczna rzędna dna", actual.ValidationIssues);
        Assert.Equal("niska", actual.Confidence);
        Assert.Equal(126.51, actual.InvertElevationM);
    }

}


public class PrefabScan37MergerTests
{
    [Fact]
    public void Healthy_Pzt_Inventory_Prevents_Profile_From_Inventing_New_Ids()
    {
        var pzt = new ParsedProject { DrawingType = "PZT", SourceFile = "PZT.pdf" };
        foreach (var id in new[] { "D1", "D2", "D3", "D4", "D5", "D6" })
            pzt.Manholes.Add(new ParsedManhole { Identifier = id, SourceDocument = "PZT.pdf" });

        var profile = new ParsedProject { DrawingType = "PROFIL", SourceFile = "profil.pdf" };
        profile.Manholes.Add(new ParsedManhole { Identifier = "D1", DiameterMm = 1200, SourceDocument = "profil.pdf" });
        profile.Manholes.Add(new ParsedManhole { Identifier = "S40", Transitions = { new ManholeTransition { Material = "PP", DiameterMm = 40, Quantity = 1 } }, SourceDocument = "profil.pdf" });

        var merged = ProjectMerger.Merge(new[] { profile, pzt });

        Assert.Contains(merged.Manholes, m => m.Identifier == "D1" && m.DiameterMm == 1200);
        Assert.DoesNotContain(merged.Manholes, m => m.Identifier == "S40");
    }
}


public class PrefabScan38MergerTests
{
    [Fact]
    public void Profile_Elevations_Override_Pzt_Elevations_When_Coherent()
    {
        var pzt = new ParsedProject { DrawingType = "PZT", SourceFile = "PZT.pdf" };
        pzt.Manholes.Add(new ParsedManhole
        {
            Identifier = "D12",
            GroundElevationM = 62.80,
            InvertElevationM = 61.13,
            HeightM = 1.67,
            SourceDocument = "PZT.pdf"
        });

        var profile = new ParsedProject { DrawingType = "PROFIL", SourceFile = "profil.pdf" };
        profile.Manholes.Add(new ParsedManhole
        {
            Identifier = "D12",
            GroundElevationM = 62.80,
            InvertElevationM = 61.24,
            HeightM = 1.56,
            SourceDocument = "profil.pdf"
        });

        var merged = ProjectMerger.Merge(new[] { pzt, profile });
        var d12 = Assert.Single(merged.Manholes);

        Assert.Equal(62.80, d12.GroundElevationM);
        Assert.Equal(61.24, d12.InvertElevationM);
        Assert.Equal(1.56, d12.HeightM);
    }

    [Fact]
    public void Profile_Only_Object_With_Two_Engineering_Field_Groups_Can_Recover_Pzt_Ocr_Miss()
    {
        var pzt = new ParsedProject { DrawingType = "PZT", SourceFile = "PZT.pdf" };
        foreach (var id in new[] { "D2", "D3", "D4", "D6", "D7", "D8" })
            pzt.Manholes.Add(new ParsedManhole { Identifier = id, SourceDocument = "PZT.pdf" });

        var profile = new ParsedProject { DrawingType = "PROFIL", SourceFile = "profil.pdf" };
        var d1 = new ParsedManhole
        {
            Identifier = "D1",
            GroundElevationM = 62.70,
            InvertElevationM = 60.49,
            HeightM = 2.21,
            SourceDocument = "profil.pdf"
        };
        d1.Transitions.Add(new ManholeTransition { Material = "PVC", DiameterMm = 160, Quantity = 1 });
        profile.Manholes.Add(d1);

        var merged = ProjectMerger.Merge(new[] { pzt, profile });

        Assert.Contains(merged.Manholes, m => m.Identifier == "D1");
    }

    [Fact]
    public void Bare_Profile_Only_Id_Is_Still_Rejected_With_Healthy_Pzt_Map()
    {
        var pzt = new ParsedProject { DrawingType = "PZT", SourceFile = "PZT.pdf" };
        foreach (var id in new[] { "D2", "D3", "D4", "D6", "D7", "D8" })
            pzt.Manholes.Add(new ParsedManhole { Identifier = id, SourceDocument = "PZT.pdf" });

        var profile = new ParsedProject { DrawingType = "PROFIL", SourceFile = "profil.pdf" };
        profile.Manholes.Add(new ParsedManhole { Identifier = "S40", SourceDocument = "profil.pdf" });

        var merged = ProjectMerger.Merge(new[] { pzt, profile });

        Assert.DoesNotContain(merged.Manholes, m => m.Identifier == "S40");
    }
}


public class PrefabScan40HeightTests
{
    [Fact]
    public void Merge_Preserves_Profile_Derived_PrefabHeight_Instead_Of_Recomputing_RawDepth()
    {
        var pzt = new ParsedProject { DrawingType = "PZT", SourceFile = "PZT.pdf" };
        pzt.Manholes.Add(new ParsedManhole
        {
            Identifier = "D4",
            GroundElevationM = 61.85,
            InvertElevationM = 60.67,
            HeightM = 1.18,
            SourceDocument = "PZT.pdf"
        });

        var profile = new ParsedProject { DrawingType = "PROFIL", SourceFile = "profil.pdf" };
        profile.Manholes.Add(new ParsedManhole
        {
            Identifier = "D4",
            GroundElevationM = 61.85,
            InvertElevationM = 60.67,
            HeightM = 1.03,
            DiameterMm = 1200,
            Type = "kinetowa",
            Crown = "pierścień+płyta_odc.",
            SourceDocument = "profil.pdf"
        });

        var merged = ProjectMerger.Merge(new[] { pzt, profile });
        var d4 = Assert.Single(merged.Manholes);

        Assert.Equal(1.03, d4.HeightM);
        Assert.Equal(1200, d4.DiameterMm);
        Assert.Equal("kinetowa", d4.Type);
        Assert.Equal("pierścień+płyta_odc.", d4.Crown);
    }
}
