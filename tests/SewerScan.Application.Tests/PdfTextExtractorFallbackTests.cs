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
}
