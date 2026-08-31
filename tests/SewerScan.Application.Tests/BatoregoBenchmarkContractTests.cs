using SewerScan.Application.DTO;
using SewerScan.Application.Services;
using Xunit;

namespace SewerScan.Application.Tests;

public class BatoregoBenchmarkContractTests
{
    [Fact]
    public void Benchmark_Uses_Reference_Height_For_D7_And_Does_Not_Confuse_Structure_With_Crown()
    {
        var project = new ParsedProject();
        project.Manholes.Add(new ParsedManhole
        {
            Identifier = "D7",
            DiameterMm = 1200,
            HeightM = 1.33,
            Type = "KINETA",
            Crown = "zwężka"
        });

        var score = BatoregoBenchmark.GetScore(project);

        Assert.Equal(1, score.Found);
        Assert.Equal(1, score.Dn);
        Assert.Equal(1, score.Height);
        Assert.Equal(1, score.Type);
        Assert.Equal(1, score.Crown);
    }
}
