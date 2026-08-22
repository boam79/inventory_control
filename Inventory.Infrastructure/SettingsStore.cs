namespace Inventory.Infrastructure;

public sealed class SettingsStore
{
    public const string ExpiryWarningDays = "ExpiryWarningDays";
    public const string BackupFolder = "BackupFolder";
    public const string PeriodYear = "PeriodYear";
    public const string PeriodMonth = "PeriodMonth";
    public const string FontScale = "FontScale";
    public const string LastBackupDate = "LastBackupDate";

    private readonly InventoryDbContext _db;

    public SettingsStore(InventoryDbContext db)
    {
        _db = db;
    }

    public string Get(string key, string defaultValue = "")
    {
        var row = _db.Settings.SingleOrDefault(s => s.Key == key);
        return row?.Value ?? defaultValue;
    }

    public int GetInt(string key, int defaultValue)
    {
        return int.TryParse(Get(key, defaultValue.ToString()), out var n) ? n : defaultValue;
    }

    public void Set(string key, string value)
    {
        var row = _db.Settings.SingleOrDefault(s => s.Key == key);
        if (row is null)
        {
            _db.Settings.Add(new AppSetting { Key = key, Value = value, Version = 1 });
        }
        else
        {
            row.Value = value;
            row.Version += 1;
        }

        _db.SaveChanges();
    }
}
