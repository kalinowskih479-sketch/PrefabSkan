using System.Security.Cryptography;

namespace SewerScan.Infrastructure.OCR;

internal static class OcrModelStore
{
    internal const string TessdataCommit = "65727574dfcd264acbb0c3e07860e4e9e9b22185";
    internal const string EnglishSha256 = "7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2";
    internal const string PolishSha256 = "C4476CDBC0E33D898D32345122B7BE1CBF85ACE15F920F06C7714756E1EF79B2";

    internal static string Resolve(string baseDirectory)
    {
        var tessdata = Path.Combine(baseDirectory, "tessdata");
        ValidateFile(Path.Combine(tessdata, "eng.traineddata"), EnglishSha256);
        ValidateFile(Path.Combine(tessdata, "pol.traineddata"), PolishSha256);
        return tessdata;
    }

    internal static void ValidateFile(string path, string expectedSha256)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Brakuje modelu OCR: {Path.GetFileName(path)}", path);

        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Model OCR {Path.GetFileName(path)} ma nieprawidłową sumę SHA-256.");
    }
}
