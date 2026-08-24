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
    public void Settings_view_hides_usage_notify_ui_but_defaults_still_send()
    {
        var settingsSource = File.ReadAllText(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "Inventory.App", "Views", "WorkspaceViews.cs")));
        var settingsStart = settingsSource.IndexOf("public sealed class SettingsView", StringComparison.Ordinal);
        Assert.True(settingsStart >= 0);
        var settingsClass = settingsSource[settingsStart..];
        Assert.DoesNotContain("Section(\"사용 알림\"", settingsClass, StringComparison.Ordinal);
        Assert.DoesNotContain("사용 알림 보내기", settingsClass, StringComparison.Ordinal);
        Assert.DoesNotContain("사용 알림 설정 저장", settingsClass, StringComparison.Ordinal);
        Assert.DoesNotContain("SMTP 발신(From)", settingsClass, StringComparison.Ordinal);
        Assert.DoesNotContain("설치 ID (읽기 전용)", settingsClass, StringComparison.Ordinal);

        var mainCs = File.ReadAllText(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "Inventory.App", "MainWindow.xaml.cs")));
        Assert.Contains("TrySendUsageHeartbeat", mainCs, StringComparison.Ordinal);
        Assert.Contains("UsageHeartbeatService.TrySendTodayInBackground", mainCs, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadOrDefault_empty_config_uses_built_in_smtp_for_send_path()
    {
        var root = NewTempRoot();
        try
        {
            Assert.False(File.Exists(UsageNotifyConfigStore.ConfigPath(root)));

            var loaded = UsageNotifyConfigStore.LoadOrDefault(root);
            Assert.True(loaded.Enabled);
            Assert.Equal(UsageNotifyDefaults.DefaultFromAddress, loaded.FromAddress);
            Assert.Equal(UsageNotifyOptions.DefaultSmtpHost, loaded.SmtpHost);
            Assert.Equal(UsageNotifyOptions.DefaultSmtpPort, loaded.SmtpPort);
            Assert.True(loaded.IsSmtpConfigured());
            Assert.Equal(UsageNotifyDefaults.GetBuiltInPassword(), loaded.ResolvePassword());

            var sender = new RecordingMailSender();
            var today = new DateOnly(2026, 8, 24);
            var payload = UsageNotifyMessage.Build(Guid.NewGuid(), sentAtLocal: today.ToDateTime(TimeOnly.Parse("09:00")));
            var result = UsageHeartbeatService.TrySendToday(
                root,
                payload,
                sender,
                todayLocal: today);

            Assert.Equal(UsageHeartbeatOutcome.Sent, result.Outcome);
            Assert.Single(sender.Calls);
            Assert.Equal(UsageNotifyDefaults.GetBuiltInPassword(), sender.Calls[0].ResolvedPassword);
            Assert.Equal(UsageNotifyDefaults.DefaultFromAddress, sender.Calls[0].FromAddress);
            Assert.Equal(UsageNotifyOptions.DefaultToAddress, loaded.ToAddress);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolvePassword_prefers_settings_over_built_in()
    {
        var options = new UsageNotifyOptions
        {
            FromAddress = UsageNotifyDefaults.DefaultFromAddress,
            Password = "custom-override-password"
        };
        Assert.Equal("custom-override-password", options.ResolvePassword());
        Assert.NotEqual(UsageNotifyDefaults.GetBuiltInPassword(), options.ResolvePassword());
    }

    [Fact]
    public void Built_in_password_is_obfuscated_not_plaintext_in_defaults_source()
    {
        var source = File.ReadAllText(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "Inventory.Infrastructure", "UsageNotifyDefaults.cs")));
        Assert.DoesNotContain(UsageNotifyDefaults.GetBuiltInPassword(), source, StringComparison.Ordinal);
        Assert.Contains("ObfuscatedPassword", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TrySendToday_sends_on_every_startup_call()
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
            Assert.Equal(UsageHeartbeatOutcome.Sent, second.Outcome);
            Assert.Equal(2, sender.Calls.Count);
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
            Assert.Equal("secret-app-password", loaded.ResolvePassword());
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
        public List<(string Subject, string Body, string? ResolvedPassword, string FromAddress)> Calls { get; } = new();

        public void Send(UsageNotifyOptions options, string subject, string body) =>
            Calls.Add((subject, body, options.ResolvePassword(), options.FromAddress));
    }
}
