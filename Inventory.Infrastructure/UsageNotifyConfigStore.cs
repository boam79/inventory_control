using System.Text.Json;

namespace Inventory.Infrastructure;

public sealed class UsageNotifyState
{
    public string? LastSentDate { get; set; }
}

/// <summary>
/// %LOCALAPPDATA%\SpringClinicInventory\usage-notify.json 및 발송 상태 파일.
/// </summary>
public static class UsageNotifyConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string DefaultAppDataRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpringClinicInventory");

    public static string ConfigPath(string appDataRoot) =>
        Path.Combine(appDataRoot, UsageNotifyOptions.ConfigFileName);

    public static string StatePath(string appDataRoot) =>
        Path.Combine(appDataRoot, UsageNotifyOptions.StateFileName);

    public static UsageNotifyOptions LoadOrDefault(string appDataRoot)
    {
        var path = ConfigPath(appDataRoot);
        if (!File.Exists(path))
        {
            return CreateBuiltInDefaults();
        }

        try
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<UsageNotifyOptions>(json, JsonOptions);
            if (loaded is null)
            {
                return CreateBuiltInDefaults();
            }

            UsageNotifyDefaults.ApplyMissingFields(loaded);
            return loaded;
        }
        catch
        {
            return CreateBuiltInDefaults();
        }
    }

    /// <summary>설정 파일 없을 때: 사용 알림 ON + 내장 From/SMTP/비밀번호.</summary>
    public static UsageNotifyOptions CreateBuiltInDefaults()
    {
        var options = new UsageNotifyOptions { Enabled = true };
        UsageNotifyDefaults.ApplyMissingFields(options);
        return options;
    }

    public static void Save(string appDataRoot, UsageNotifyOptions options, string? plainPasswordToProtect = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        Directory.CreateDirectory(appDataRoot);

        if (!string.IsNullOrEmpty(plainPasswordToProtect))
        {
            options.PasswordProtected = UsageNotifySecrets.Protect(plainPasswordToProtect);
            options.Password = null;
        }

        // 저장 시 평문 Password는 파일에 남기지 않음(보호값만). 단, 보호 불가 환경에서만 Password 유지.
        var toWrite = new UsageNotifyOptions
        {
            Enabled = options.Enabled,
            ToAddress = string.IsNullOrWhiteSpace(options.ToAddress)
                ? UsageNotifyOptions.DefaultToAddress
                : options.ToAddress.Trim(),
            SmtpHost = string.IsNullOrWhiteSpace(options.SmtpHost)
                ? UsageNotifyOptions.DefaultSmtpHost
                : options.SmtpHost.Trim(),
            SmtpPort = options.SmtpPort > 0 ? options.SmtpPort : UsageNotifyOptions.DefaultSmtpPort,
            FromAddress = options.FromAddress?.Trim() ?? string.Empty,
            PasswordProtected = options.PasswordProtected,
            Password = OperatingSystem.IsWindows() ? null : options.Password
        };

        File.WriteAllText(ConfigPath(appDataRoot), JsonSerializer.Serialize(toWrite, JsonOptions));
    }

    public static UsageNotifyState LoadState(string appDataRoot)
    {
        var path = StatePath(appDataRoot);
        if (!File.Exists(path))
        {
            return new UsageNotifyState();
        }

        try
        {
            return JsonSerializer.Deserialize<UsageNotifyState>(File.ReadAllText(path), JsonOptions)
                   ?? new UsageNotifyState();
        }
        catch
        {
            return new UsageNotifyState();
        }
    }

    public static void SaveState(string appDataRoot, UsageNotifyState state)
    {
        Directory.CreateDirectory(appDataRoot);
        File.WriteAllText(StatePath(appDataRoot), JsonSerializer.Serialize(state, JsonOptions));
    }

    public static DateOnly? ParseLastSentDate(UsageNotifyState state)
    {
        if (state.LastSentDate is null)
        {
            return null;
        }

        return DateOnly.TryParse(state.LastSentDate, out var d) ? d : null;
    }
}
