namespace Inventory.Infrastructure;

/// <summary>
/// Marks the planned persistence engine. EF Core SQLite is wired in task 1.2.
/// </summary>
public static class DataStoreMarker
{
    public const string Engine = "SQLite";
}
