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
        Assert.Equal("1.0.17", ProductInfo.Version);
        Assert.Equal("SQLite", DataStoreMarker.Engine);
    }

    [Fact]
    public void Installer_pack_script_exists()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? script = null;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "pack-installer.ps1");
            if (File.Exists(candidate))
            {
                script = candidate;
                break;
            }

            dir = dir.Parent;
        }

        Assert.False(string.IsNullOrEmpty(script));
        var text = File.ReadAllText(script!);
        Assert.Contains("vpk pack", text);
        Assert.Contains("Setup.exe", text);
        Assert.Contains("releases.win.json", text);
        Assert.Contains("assets.win.json", text);
    }
}
