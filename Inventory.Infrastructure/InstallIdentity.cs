namespace Inventory.Infrastructure;

/// <summary>
/// PC당 익명 Install ID(GUID). AppData에 한 번 생성 후 유지합니다.
/// </summary>
public static class InstallIdentity
{
    public static Guid GetOrCreate(string appDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        Directory.CreateDirectory(appDataRoot);
        var path = Path.Combine(appDataRoot, UsageNotifyOptions.InstallIdFileName);
        if (File.Exists(path))
        {
            var text = File.ReadAllText(path).Trim();
            if (Guid.TryParse(text, out var existing) && existing != Guid.Empty)
            {
                return existing;
            }
        }

        var created = Guid.NewGuid();
        File.WriteAllText(path, created.ToString("D"));
        return created;
    }
}
