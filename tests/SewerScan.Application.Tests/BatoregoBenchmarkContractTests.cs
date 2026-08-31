using SewerScan.Application.DTO;
using SewerScan.Application.Services;
using Xunit;

namespace SewerScan.Application.Tests;

public class BatoregoBenchmarkContractTests
{
    [Fact]
    public void Benchmark_Accepts_Concrete_Manhole_With_CastIron_Cover_As_Batorego_Kineta_With_Cone()
    {
        var project = new ParsedProject();
        project.Manholes.Add(new ParsedManhole
        {
            Identifier = "D7",
            DiameterMm = 1200,
            HeightM = 1.33,
            Type = "betonowa",
            Crown = "właz żeliwny"
        });

        var score = BatoregoBenchmark.GetScore(project);

        Assert.Equal(1, score.Found);
        Assert.Equal(1, score.Dn);
        Assert.Equal(1, score.Height);
        Assert.Equal(1, score.Type);
        Assert.Equal(1, score.Crown);
    }
}
