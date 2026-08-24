namespace Inventory.Infrastructure;

/// <summary>
/// SMTP 사용 알림(하트비트) 설정. 비밀번호는 소스에 넣지 말고
/// %LOCALAPPDATA%\SpringClinicInventory\usage-notify.json 또는 설정 화면에서만 보관하세요.
/// 한메일(다음)은 일반 비밀번호가 아니라 앱 비밀번호가 필요합니다.
/// </summary>
public sealed class UsageNotifyOptions
{
    public const string DefaultToAddress = "pjm7908@hanmail.net";
    public const string DefaultSmtpHost = "smtp.daum.net";
    public const int DefaultSmtpPort = 465;
    public const string ConfigFileName = "usage-notify.json";
    public const string StateFileName = "usage-notify-state.json";
    public const string InstallIdFileName = "install-id.txt";

    /// <summary>기본 ON. SMTP 미설정이면 발송만 건너뜁니다.</summary>
    public bool Enabled { get; set; } = true;

    public string ToAddress { get; set; } = DefaultToAddress;
    public string SmtpHost { get; set; } = DefaultSmtpHost;
    public int SmtpPort { get; set; } = DefaultSmtpPort;

    /// <summary>발신 주소(보통 한메일 계정과 동일).</summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    /// 평문 비밀번호(로컬 AppData JSON 전용). DPAPI 값이 있으면 무시됩니다.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>Windows DPAPI로 보호한 비밀번호(Base64).</summary>
    public string? PasswordProtected { get; set; }

    public bool IsSmtpConfigured()
    {
        if (string.IsNullOrWhiteSpace(FromAddress) || string.IsNullOrWhiteSpace(SmtpHost) || SmtpPort <= 0)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(ResolvePassword());
    }

    public string? ResolvePassword()
    {
        if (!string.IsNullOrWhiteSpace(PasswordProtected)
            && UsageNotifySecrets.TryUnprotect(PasswordProtected, out var plain)
            && !string.IsNullOrWhiteSpace(plain))
        {
            return plain;
        }

        return string.IsNullOrWhiteSpace(Password) ? null : Password;
    }
}
