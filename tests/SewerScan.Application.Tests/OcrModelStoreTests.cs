using System.Security.Cryptography;
using SewerScan.Infrastructure.OCR;
using Xunit;

namespace SewerScan.Application.Tests;

public sealed class OcrModelStoreTests
{
    [Fact]
    public void ValidateFile_AcceptsMatchingSha256()
    {
        var path = CreateTemporaryFile([1, 2, 3, 4]);
        try
        {
            var expected = Convert.ToHexString(SHA256.HashData([1, 2, 3, 4]));
            OcrModelStore.ValidateFile(path, expected);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Resolve_DoesNotDownloadMissingModels()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var ex = Assert.Throws<FileNotFoundException>(() => OcrModelStore.Resolve(directory));
            Assert.Contains("eng.traineddata", ex.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Resolve_RejectsModelWithWrongChecksum()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var tessdata = Directory.CreateDirectory(Path.Combine(directory, "tessdata")).FullName;
            File.WriteAllBytes(Path.Combine(tessdata, "eng.traineddata"), [1, 2, 3, 4]);

            var ex = Assert.Throws<InvalidDataException>(() => OcrModelStore.Resolve(directory));
            Assert.Contains("SHA-256", ex.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"PrefabScan-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateTemporaryFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"PrefabScan-tests-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
