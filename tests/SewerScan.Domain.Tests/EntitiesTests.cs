using System;
using SewerScan.Domain.Entities;
using Xunit;

namespace SewerScan.Domain.Tests;

public class EntitiesTests
{
    [Fact]
    public void ProjectNameValidationThrows()
    {
        Assert.Throws<ArgumentException>(() => new Project(""));
    }

    [Fact]
    public void CoordinateValidationThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Coordinate(double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Coordinate(0, double.PositiveInfinity));
    }

    [Fact]
    public void DiameterValidationThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Diameter(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Diameter(-10));
    }

    [Fact]
    public void CreateManholeAndAddInlet()
    {
        var coord = new Coordinate(100, 200);
        var elev = new Elevation(12.5);
        var manhole = new Manhole(coord, elev);
        var inlet = new Inlet(new Coordinate(101, 201), new Elevation(12.4));
        manhole.AddInlet(inlet);
        Assert.Single(manhole.Inlets);
    }

    [Fact]
    public void CreatePipeRequiresDifferentEndpoints()
    {
        var start = PipeConnection.FromCoordinate(new Coordinate(0, 0));
        var end = PipeConnection.FromCoordinate(new Coordinate(1, 1));
        var diameter = new Diameter(200);
        var pipe = new Pipe(start, end, diameter, PipeMaterial.PVC, PipeDirection.Both);
        Assert.Equal(PipeMaterial.PVC, pipe.Material);
    }
}
