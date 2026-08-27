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
        Assert.Equal("1.0.52", ProductInfo.Version);
        Assert.Equal("SQLite", DataStoreMarker.Engine);
    }

    [Fact]
    public void Installer_pack_script_exists()
    {
        var text = ReadRepoFile("scripts", "pack-installer.ps1");
        Assert.Contains("vpk pack", text);
        Assert.Contains("Setup.exe", text);
        Assert.Contains("releases.win.json", text);
        Assert.Contains("assets.win.json", text);
    }

    [Fact]
    public void App_csproj_keeps_wpf_on_windows_and_a_stub_elsewhere()
    {
        var text = ReadRepoFile("Inventory.App", "Inventory.App.csproj");
        Assert.Contains("net10.0-windows", text);
        Assert.Contains("MacDevPlaceholder.cs", text);
        Assert.Contains("IsOSPlatform('Windows')", text);
        Assert.Contains("EnableDefaultItems", text);
    }

    [Fact]
    public void Cross_platform_dev_scripts_and_ci_exist()
    {
        var env = ReadRepoFile("scripts", "mac-env.sh");
        Assert.Contains("DOTNET_ROOT", env);
        var unix = ReadRepoFile("scripts", "dev.sh");
        Assert.Contains("dotnet test", unix);
        var win = ReadRepoFile("scripts", "dev.ps1");
        Assert.Contains("dotnet test", win);
        var wrapper = ReadRepoFile("scripts", "dev-mac.sh");
        Assert.Contains("dev.sh", wrapper);
        var ci = ReadRepoFile(".github", "workflows", "ci.yml");
        Assert.Contains("windows-latest", ci);
        Assert.Contains("macos-latest", ci);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
