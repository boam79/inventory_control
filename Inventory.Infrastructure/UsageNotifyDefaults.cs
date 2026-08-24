using System.Text;

namespace Inventory.Infrastructure;

/// <summary>
/// 사용 알림 SMTP 내장 기본값(의원 PC에서 설정 없이 발송).
/// 앱 비밀번호는 평문 const가 아니라 XOR+Base64로만 보관합니다.
/// 디컴파일로 복원 가능하므로 개발자 전용 send-only 앱 비밀번호로만 쓰세요.
/// </summary>
public static class UsageNotifyDefaults
{
    public const string DefaultFromAddress = "pjm7908@hanmail.net";
    public const string DefaultSmtpUser = "pjm7908";

    /// <summary>컴파일 타임 난독화 키(비밀번호 자체는 아님).</summary>
    private static readonly byte[] EmbedKey = Encoding.UTF8.GetBytes("SpringClinic.UsageNotify.Embed.v1");

    /// <summary>내장 앱 비밀번호의 XOR+Base64 블롭.</summary>
    private static readonly byte[] ObfuscatedPassword = Convert.FromBase64String("KhYZGQIROgoDAAMQSzYYBA==");

    public static string GetBuiltInPassword()
    {
        var plain = new byte[ObfuscatedPassword.Length];
        for (var i = 0; i < ObfuscatedPassword.Length; i++)
        {
            plain[i] = (byte)(ObfuscatedPassword[i] ^ EmbedKey[i % EmbedKey.Length]);
        }

        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>설정에 From이 비어 있으면 내장 발신 주소를 씁니다.</summary>
    public static void ApplyMissingFields(UsageNotifyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ToAddress))
        {
            options.ToAddress = UsageNotifyOptions.DefaultToAddress;
        }

        if (string.IsNullOrWhiteSpace(options.SmtpHost))
        {
            options.SmtpHost = UsageNotifyOptions.DefaultSmtpHost;
        }

        if (options.SmtpPort <= 0)
        {
            options.SmtpPort = UsageNotifyOptions.DefaultSmtpPort;
        }

        if (string.IsNullOrWhiteSpace(options.FromAddress))
        {
            options.FromAddress = DefaultFromAddress;
        }
    }
}
