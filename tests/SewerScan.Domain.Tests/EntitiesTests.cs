using SewerScan.Domain.Entities;
using Xunit;

namespace SewerScan.Domain.Tests;

public class EntitiesTests
{
    [Fact]
    public void CanCreateEntities()
    {
        _ = new Project();
        _ = new Drawing();
        _ = new Profile();
        _ = new Manhole();
        _ = new Inlet();
        _ = new Pipe();
        _ = new Tender();
        _ = new DetectionResult();
        _ = new ExportJob();
        _ = new UserSettings();
    }
}
