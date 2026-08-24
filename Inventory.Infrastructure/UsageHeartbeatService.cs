using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Serilog;

namespace Inventory.Infrastructure;

public interface IUsageMailSender
{
    void Send(UsageNotifyOptions options, string subject, string body);
}

/// <summary>한메일(다음) SMTP — 기본 smtp.daum.net:465 SSL(암시적). 587은 STARTTLS.</summary>
public sealed class SmtpUsageMailSender : IUsageMailSender
{
    public void Send(UsageNotifyOptions options, string subject, string body)
    {
        var password = options.ResolvePassword()
                       ?? throw new InvalidOperationException("SMTP 비밀번호가 없습니다.");
        var from = options.FromAddress.Trim();
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(options.ToAddress.Trim()));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        var secure = options.SmtpPort == 465
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

        using var client = new SmtpClient { Timeout = 15_000 };
        client.Connect(options.SmtpHost.Trim(), options.SmtpPort, secure);
        try
        {
            client.Authenticate(from, password);
            client.Send(message);
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect(true);
            }
        }
    }
}

public enum UsageHeartbeatOutcome
{
    Sent,
    SkippedDisabled,
    SkippedAlreadySentToday,
    SkippedSmtpNotConfigured,
    Failed
}

public sealed record UsageHeartbeatResult(UsageHeartbeatOutcome Outcome, string Message);

/// <summary>
/// 설치/사용 하트비트 메일. 하루 1회, 실패해도 업무를 막지 않습니다. 재고 데이터는 보내지 않습니다.
/// </summary>
public static class UsageHeartbeatService
{
    private static int _smtpMissingLogged;

    public static bool ShouldSendToday(DateOnly? lastSentLocalDate, DateOnly todayLocal) =>
        lastSentLocalDate is null || lastSentLocalDate.Value != todayLocal;

    public static UsageHeartbeatResult TrySendToday(
        string appDataRoot,
        UsageNotifyPayload payload,
        IUsageMailSender? sender = null,
        ILogger? log = null,
        DateOnly? todayLocal = null,
        UsageNotifyOptions? optionsOverride = null)
    {
        try
        {
            var options = optionsOverride ?? UsageNotifyConfigStore.LoadOrDefault(appDataRoot);
            if (!options.Enabled)
            {
                return new UsageHeartbeatResult(UsageHeartbeatOutcome.SkippedDisabled, "사용 알림이 꺼져 있습니다.");
            }

            var today = todayLocal ?? DateOnly.FromDateTime(DateTime.Now);
            var state = UsageNotifyConfigStore.LoadState(appDataRoot);
            var last = UsageNotifyConfigStore.ParseLastSentDate(state);
            if (!ShouldSendToday(last, today))
            {
                return new UsageHeartbeatResult(
                    UsageHeartbeatOutcome.SkippedAlreadySentToday,
                    "오늘은 이미 사용 알림을 보냈습니다.");
            }

            if (!options.IsSmtpConfigured())
            {
                if (Interlocked.Exchange(ref _smtpMissingLogged, 1) == 0)
                {
                    log?.Information(
                        "사용 알림 SMTP 미설정 — 발송 생략. 설정>사용 알림 또는 usage-notify.json에 From/앱 비밀번호를 넣으세요.");
                }

                return new UsageHeartbeatResult(
                    UsageHeartbeatOutcome.SkippedSmtpNotConfigured,
                    "SMTP가 설정되지 않아 발송하지 않았습니다.");
            }

            var mail = sender ?? new SmtpUsageMailSender();
            var subject = UsageNotifyMessage.BuildSubject(payload.InstallId);
            var body = UsageNotifyMessage.BuildBody(payload);
            mail.Send(options, subject, body);

            state.LastSentDate = today.ToString("yyyy-MM-dd");
            UsageNotifyConfigStore.SaveState(appDataRoot, state);
            log?.Information("사용 알림 메일 발송 완료 InstallId={InstallId}", UsageNotifyMessage.ShortInstallId(payload.InstallId));
            return new UsageHeartbeatResult(UsageHeartbeatOutcome.Sent, "사용 알림을 보냈습니다.");
        }
        catch (Exception ex)
        {
            log?.Warning(ex, "사용 알림 메일 실패(업무는 계속): {Message}", AppLog.Sanitize(ex.Message));
            return new UsageHeartbeatResult(UsageHeartbeatOutcome.Failed, AppLog.Sanitize(ex.Message));
        }
    }

    /// <summary>시작 시 백그라운드 발송. 예외를 밖으로 던지지 않습니다.</summary>
    public static void TrySendTodayInBackground(
        string appDataRoot,
        string? clinicName,
        ILogger? log)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var installId = InstallIdentity.GetOrCreate(appDataRoot);
                var payload = UsageNotifyMessage.Build(
                    installId,
                    machineName: Environment.MachineName,
                    windowsUserName: Environment.UserName,
                    clinicName: clinicName);
                TrySendToday(appDataRoot, payload, log: log);
            }
            catch (Exception ex)
            {
                log?.Warning(ex, "사용 알림 백그라운드 실패: {Message}", AppLog.Sanitize(ex.Message));
            }
        });
    }
}
