using Inventory.Core;
using System.Globalization;
using System.Text;

namespace Inventory.Infrastructure;

public sealed record UsageNotifyPayload(
    Guid InstallId,
    string AppVersion,
    DateTime SentAtLocal,
    string? MachineName,
    string? WindowsUserName,
    string? ClinicName);

public static class UsageNotifyMessage
{
    public static string ShortInstallId(Guid installId) =>
        installId.ToString("N")[..8];

    public static string BuildSubject(Guid installId) =>
        $"[스프링의원 재고] 사용 알림 · {ShortInstallId(installId)}";

    public static UsageNotifyPayload Build(
        Guid installId,
        string? appVersion = null,
        DateTime? sentAtLocal = null,
        string? machineName = null,
        string? windowsUserName = null,
        string? clinicName = null) =>
        new(
            installId,
            appVersion ?? ProductInfo.Version,
            sentAtLocal ?? DateTime.Now,
            machineName,
            windowsUserName,
            clinicName);

    public static string BuildBody(UsageNotifyPayload payload)
    {
        var sb = new StringBuilder();
        sb.AppendLine("스프링의원 재고관리 — 사용(설치) 알림");
        sb.AppendLine();
        sb.AppendLine($"설치 ID: {payload.InstallId:D}");
        sb.AppendLine($"앱 버전: {payload.AppVersion}");
        sb.AppendLine($"전송 시각: {payload.SentAtLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(payload.MachineName))
        {
            sb.AppendLine($"PC 이름: {payload.MachineName}");
        }

        if (!string.IsNullOrWhiteSpace(payload.WindowsUserName))
        {
            sb.AppendLine($"Windows 사용자: {payload.WindowsUserName}");
        }

        if (!string.IsNullOrWhiteSpace(payload.ClinicName))
        {
            sb.AppendLine($"의원/앱 이름: {payload.ClinicName}");
        }

        sb.AppendLine();
        sb.AppendLine("※ 재고·거래 등 업무 데이터는 포함되지 않습니다.");
        return sb.ToString();
    }
}
