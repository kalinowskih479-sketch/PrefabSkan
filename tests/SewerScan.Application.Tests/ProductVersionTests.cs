using SewerScan.Shared.Utilities;
using Xunit;

namespace SewerScan.Application.Tests;

public sealed class ProductVersionTests
{
    [Fact]
    public void ProductVersion_Is_4_2_8()
    {
        Assert.Equal("4.2.8", ProductInfo.Version);
        Assert.Equal("PrefabScan 4.2.8", ProductInfo.DisplayName);
    }
}
