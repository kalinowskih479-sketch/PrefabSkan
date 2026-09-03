using SewerScan.Infrastructure.OCR;
using Xunit;

namespace SewerScan.Application.Tests;

public sealed class OcrCacheIdentityTests
{
    [Fact]
    public void CachePath_ContainsSchemaAndAlgorithmVersions()
    {
        var path = OcrCacheIdentity.CreatePath("cache", "ABC123");

        Assert.Equal(Path.Combine("cache", "ocr-schema-1_algorithm-4.2.7_ABC123.json"), path);
        Assert.DoesNotContain("4.1-profile", path);
    }
}
