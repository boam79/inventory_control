namespace Inventory.Core;

public static class SqliteConnectionString
{
    public static string FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return $"Data Source={path}";
    }

    public static string DefaultDatabasePath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpringClinicInventory");
        return Path.Combine(folder, "inventory.db");
    }
}
