using Inventory.Core;
using Inventory.Infrastructure;

namespace Inventory.Tests;

public class SolutionSkeletonTests
{
    [Fact]
    public void Core_and_Infrastructure_project_references_load()
    {
        Assert.Equal("SpringClinic.Inventory", ProductInfo.Name);
        Assert.Equal("스프링의원 재고관리", ProductInfo.DisplayName);
        Assert.Equal("SQLite", DataStoreMarker.Engine);
    }
}
