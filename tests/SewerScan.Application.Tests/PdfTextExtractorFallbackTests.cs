using System;
using System.Linq;
using SewerScan.Application.Models;
using SewerScan.Infrastructure.Pdf;
using Xunit;

namespace SewerScan.Application.Tests;

public class PdfTextExtractorFallbackTests
{
    [Fact]
    public void Sparse_Profile_Title_Block_Without_Engineering_Candidates_Requires_Ocr()
    {
        var page = new PageText
        {
            Text = "TYTUŁ RYS.: PROFILE PODŁUŻNE SIECI KANALIZACJI DESZCZOWEJ PROJEKT TECHNICZNY"
        };

        Assert.True(PdfTextExtractor.NeedsProfileOcrFallback(new[] { page }));
    }

    [Fact]
    public void Profile_With_Engineering_Identifiers_Does_Not_Require_Ocr_Fallback()
    {
        var page = new PageText
        {
            Text = "PROFIL KANALIZACJI DESZCZOWEJ KD1 KD2 DN200"
        };

        Assert.False(PdfTextExtractor.NeedsProfileOcrFallback(new[] { page }));
    }

    [Fact]
    public void NonProfile_Document_Does_Not_Require_Profile_Ocr_Fallback()
    {
        var page = new PageText { Text = "PROJEKT TECHNICZNY DROGI I UZBROJENIA TERENU" };

        Assert.False(PdfTextExtractor.NeedsProfileOcrFallback(new[] { page }));
    }

    [Fact]
    public void Sparse_Profile_Title_Page_Does_Not_Replace_Other_Useful_Vector_Pages()
    {
        var pages = new[]
        {
            new PageText { Text = "PROFILE PODŁUŻNE SIECI KANALIZACJI DESZCZOWEJ" },
            new PageText { Text = "KD1 KD2 KD3 DN300 PVC" }
        };

        Assert.False(PdfTextExtractor.NeedsProfileOcrFallback(pages));
    }

    [Fact]
    public void Profile_Title_With_Only_D400_Cover_Class_Still_Requires_Ocr()
    {
        var page = new PageText
        {
            Text = "PROFILE PODŁUŻNE SIECI KANALIZACJI DESZCZOWEJ studnia z włazem klasy D400"
        };

        Assert.True(PdfTextExtractor.NeedsProfileOcrFallback(new[] { page }));
    }

    [Fact]
    public void Mixed_Document_Requires_Fallback_When_One_Page_Is_Empty()
    {
        var pages = new[]
        {
            new PageText { PageNumber = 1, Text = "KD1 DN1000 PVC DN200" },
            new PageText { PageNumber = 2, Text = "" }
        };

        Assert.False(PdfTextExtractor.AllPagesUseful(pages));
    }

    [Fact]
    public void Merge_Replaces_Only_Missing_Scan_Page_And_Keeps_Page_Order()
    {
        var vector = new[]
        {
            new PageText { PageNumber = 1, Text = "KD1 DN1000" },
            new PageText { PageNumber = 2, Text = "" }
        };
        var ocr = new[]
        {
            new PageText { PageNumber = 2, Text = "KS2 DN1200" },
            new PageText { PageNumber = 1, Text = "OCR KD1 DN1000" }
        };

        var result = PdfTextExtractor.MergePreferredPages(vector, ocr);

        Assert.Equal(new[] { 1, 2 }, result.Select(p => p.PageNumber));
        Assert.Same(vector[0], result[0]);
        Assert.Same(ocr[0], result[1]);
        Assert.True(PdfTextExtractor.AllPagesUseful(result));
    }

    [Fact]
    public void Merge_Replaces_Sparse_Profile_Title_With_Engineering_Ocr()
    {
        var vector = new[]
        {
            new PageText { PageNumber = 1, Text = "PROFILE KANALIZACJI DESZCZOWEJ" }
        };
        var ocr = new[]
        {
            new PageText { PageNumber = 1, Text = "PROFIL KANALIZACJI DESZCZOWEJ KD4 DN1000" }
        };

        var result = PdfTextExtractor.MergePreferredPages(vector, ocr);

        Assert.Same(ocr[0], Assert.Single(result));
    }

    [Fact]
    public void Merge_Keeps_Unresolved_Page_For_Diagnostics()
    {
        var vector = new[]
        {
            new PageText { PageNumber = 1, Text = "KD1 DN1000" },
            new PageText { PageNumber = 2, Text = "" }
        };

        var result = PdfTextExtractor.MergePreferredPages(vector, Array.Empty<PageText>());

        Assert.Equal(2, result.Count);
        Assert.False(PdfTextExtractor.AllPagesUseful(result));
    }

    [Fact]
    public void Merge_Tolerates_Duplicate_Page_Numbers_In_Extractor_Output()
    {
        var pages = new[]
        {
            new PageText { PageNumber = 1, Text = "KD1 DN1000" },
            new PageText { PageNumber = 1, Text = "KD1 DN1000" }
        };

        var result = PdfTextExtractor.MergePreferredPages(pages, Array.Empty<PageText>());

        Assert.Single(result);
        Assert.Equal(1, result[0].PageNumber);
    }
}
