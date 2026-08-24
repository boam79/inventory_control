using Inventory.Core;
using Inventory.Infrastructure;
using System.Globalization;

namespace Inventory.Tests;

public sealed class UsageHeartbeatTests
{
    [Fact]
    public void InstallIdentity_persists_same_guid()
    {
        var root = NewTempRoot();
        try
        {
            var first = InstallIdentity.GetOrCreate(root);
            var second = InstallIdentity.GetOrCreate(root);
            Assert.Equal(first, second);
            Assert.NotEqual(Guid.Empty, first);
            Assert.True(File.Exists(Path.Combine(root, UsageNotifyOptions.InstallIdFileName)));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ShouldSendToday_throttles_once_per_day()
    {
        var today = new DateOnly(2026, 8, 24);
        Assert.True(UsageHeartbeatService.ShouldSendToday(null, today));
        Assert.False(UsageHeartbeatService.ShouldSendToday(today, today));
        Assert.True(UsageHeartbeatService.ShouldSendToday(today.AddDays(-1), today));
    }

    [Fact]
    public void Payload_builder_subject_and_body_are_korean_labels()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var sent = new DateTime(2026, 8, 24, 13, 30, 0);
        var payload = UsageNotifyMessage.Build(
            id,
            appVersion: ProductInfo.Version,
            sentAtLocal: sent,
            machineName: "CLINIC-PC",
            windowsUserName: "nurse",
            clinicName: ProductInfo.DisplayName);

        Assert.Equal(ProductInfo.Version, payload.AppVersion);
        Assert.Equal("CLINIC-PC", payload.MachineName);
        Assert.Equal("nurse", payload.WindowsUserName);

        var subject = UsageNotifyMessage.BuildSubject(id);
        Assert.Contains("스프링의원 재고", subject, StringComparison.Ordinal);
        Assert.Contains("aaaaaaaa", subject, StringComparison.Ordinal);

        var body = UsageNotifyMessage.BuildBody(payload);
        Assert.Contains("설치 ID:", body, StringComparison.Ordinal);
        Assert.Contains("앱 버전:", body, StringComparison.Ordinal);
        Assert.Contains("전송 시각:", body, StringComparison.Ordinal);
        Assert.Contains("PC 이름:", body, StringComparison.Ordinal);
        Assert.Contains("Windows 사용자:", body, StringComparison.Ordinal);
        Assert.Contains("의원/앱 이름:", body, StringComparison.Ordinal);
        Assert.Contains(id.ToString("D"), body, StringComparison.Ordinal);
        Assert.Contains(ProductInfo.Version, body, StringComparison.Ordinal);
        Assert.Contains(sent.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), body, StringComparison.Ordinal);
        Assert.DoesNotContain("환자", body, StringComparison.Ordinal);
        Assert.DoesNotContain("입고", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TrySendToday_skips_when_smtp_not_configured()
    {
        var root = NewTempRoot();
        try
        {
            var sender = new RecordingMailSender();
            var payload = UsageNotifyMessage.Build(Guid.NewGuid());
            var result = UsageHeartbeatService.TrySendToday(
                root,
                payload,
                sender,
                optionsOverride: new UsageNotifyOptions { Enabled = true, FromAddress = "" });

            Assert.Equal(UsageHeartbeatOutcome.SkippedSmtpNotConfigured, result.Outcome);
            Assert.Empty(sender.Calls);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void TrySendToday_sends_once_then_throttles_same_day()
    {
        var root = NewTempRoot();
        try
        {
            var sender = new RecordingMailSender();
            var options = new UsageNotifyOptions
            {
                Enabled = true,
                FromAddress = "pjm7908@hanmail.net",
                Password = "app-password-not-real",
                SmtpHost = "smtp.daum.net",
                SmtpPort = 465
            };
            var today = new DateOnly(2026, 8, 24);
            var payload = UsageNotifyMessage.Build(Guid.NewGuid(), sentAtLocal: today.ToDateTime(TimeOnly.Parse("09:00")));

            var first = UsageHeartbeatService.TrySendToday(root, payload, sender, todayLocal: today, optionsOverride: options);
            var second = UsageHeartbeatService.TrySendToday(root, payload, sender, todayLocal: today, optionsOverride: options);

            Assert.Equal(UsageHeartbeatOutcome.Sent, first.Outcome);
            Assert.Equal(UsageHeartbeatOutcome.SkippedAlreadySentToday, second.Outcome);
            Assert.Single(sender.Calls);
            Assert.Contains("스프링의원 재고", sender.Calls[0].Subject, StringComparison.Ordinal);
            Assert.Contains("설치 ID:", sender.Calls[0].Body, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Config_save_roundtrip_without_committing_secrets_to_repo_path()
    {
        var root = NewTempRoot();
        try
        {
            var options = new UsageNotifyOptions
            {
                Enabled = true,
                FromAddress = "pjm7908@hanmail.net",
                SmtpHost = "smtp.daum.net",
                SmtpPort = 465
            };
            UsageNotifyConfigStore.Save(root, options, plainPasswordToProtect: "secret-app-password");
            var loaded = UsageNotifyConfigStore.LoadOrDefault(root);
            Assert.True(loaded.Enabled);
            Assert.Equal("pjm7908@hanmail.net", loaded.FromAddress);
            Assert.True(loaded.IsSmtpConfigured());
            Assert.Null(loaded.Password);
            Assert.False(string.IsNullOrWhiteSpace(loaded.PasswordProtected));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), "sci-usage-" + Guid.NewGuid().ToString("N"));

    private static void TryDelete(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // temp cleanup best-effort
        }
    }

    private sealed class RecordingMailSender : IUsageMailSender
    {
        public List<(string Subject, string Body)> Calls { get; } = new();

        public void Send(UsageNotifyOptions options, string subject, string body) =>
            Calls.Add((subject, body));
    }
}
