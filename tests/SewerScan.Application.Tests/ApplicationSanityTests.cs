using SewerScan.Application.Interfaces;
using Xunit;

namespace SewerScan.Application.Tests;

public class ApplicationSanityTests
{
    [Fact]
    public void MarkerInterfaceExists()
    {
        Assert.True(typeof(IApplicationMarker).IsInterface);
    }
}
