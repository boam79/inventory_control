namespace Inventory.App;

/// <summary>
/// WPF UI compiles only on Windows. This type exists so the App project can
/// still load and build on macOS/Linux for domain work and tests.
/// </summary>
internal static class MacDevPlaceholder
{
    public const string Message =
        "Inventory.App UI requires Windows. Run Inventory.Tests on this OS.";
}
