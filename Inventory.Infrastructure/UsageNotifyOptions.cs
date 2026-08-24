namespace Inventory.Infrastructure;

/// <summary>
/// SMTP 사용 알림(하트비트) 설정.
/// 기본 From/앱 비밀번호는 <see cref="UsageNotifyDefaults"/>에 내장되어 의원 PC 설정 없이 발송됩니다.
/// 설정 화면·AppData JSON으로 덮어쓸 수 있으며, 비밀번호를 비우면 내장 기본값으로 돌아갑니다.
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

    /// <summary>기본 ON. 내장 SMTP 기본값으로 발송합니다(설정에서 끌 수 있음).</summary>
    public bool Enabled { get; set; } = true;

    public string ToAddress { get; set; } = DefaultToAddress;
    public string SmtpHost { get; set; } = DefaultSmtpHost;
    public int SmtpPort { get; set; } = DefaultSmtpPort;

    /// <summary>발신 주소(보통 한메일 계정과 동일). 비우면 내장 기본 From.</summary>
    public string FromAddress { get; set; } = UsageNotifyDefaults.DefaultFromAddress;

    /// <summary>
    /// 평문 비밀번호(로컬 AppData JSON 전용). DPAPI 값이 있으면 무시됩니다.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>Windows DPAPI로 보호한 비밀번호(Base64). 없으면 내장 기본 비밀번호.</summary>
    public string? PasswordProtected { get; set; }

    public bool IsSmtpConfigured()
    {
        if (string.IsNullOrWhiteSpace(FromAddress) || string.IsNullOrWhiteSpace(SmtpHost) || SmtpPort <= 0)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(ResolvePassword());
    }

    /// <summary>
    /// 설정(DPAPI/평문) 비밀번호가 있으면 그것을, 없으면 내장 기본 앱 비밀번호를 반환합니다.
    /// </summary>
    public string? ResolvePassword()
    {
        if (!string.IsNullOrWhiteSpace(PasswordProtected)
            && UsageNotifySecrets.TryUnprotect(PasswordProtected, out var plain)
            && !string.IsNullOrWhiteSpace(plain))
        {
            return plain;
        }

        if (!string.IsNullOrWhiteSpace(Password))
        {
            return Password;
        }

        return UsageNotifyDefaults.GetBuiltInPassword();
    }
}
