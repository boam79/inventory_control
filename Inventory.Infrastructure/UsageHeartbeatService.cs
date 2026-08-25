using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Serilog;

namespace Inventory.Infrastructure;

public interface IUsageMailSender
{
    void Send(UsageNotifyOptions options, string subject, string body);
}

/// <summary>한메일(다음) SMTP — 기본 smtp.daum.net:465 + 암시적 SSL(SslOnConnect).</summary>
public sealed class SmtpUsageMailSender : IUsageMailSender
{
    public void Send(UsageNotifyOptions options, string subject, string body)
    {
        var password = options.ResolvePassword()
                       ?? throw new InvalidOperationException("SMTP 비밀번호가 없습니다.");

        var fromAddress = options.FromAddress.Trim();
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(fromAddress));
        message.To.Add(MailboxAddress.Parse(options.ToAddress.Trim()));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient { Timeout = 15_000 };
        client.Connect(
            options.SmtpHost.Trim(),
            options.SmtpPort,
            ResolveSecureSocketOptions(options.SmtpPort));
        client.Authenticate(ResolveSmtpUsername(fromAddress), password);
        client.Send(message);
        client.Disconnect(quit: true);
    }

    /// <summary>465=암시적 SSL(SslOnConnect), 587=STARTTLS, 그 외는 자동.</summary>
    public static SecureSocketOptions ResolveSecureSocketOptions(int port) =>
        port switch
        {
            465 => SecureSocketOptions.SslOnConnect,
            587 => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.Auto
        };

    /// <summary>다음 SMTP 인증 ID — 아이디만(pjm7908) 또는 전체 주소 모두 허용.</summary>
    public static string ResolveSmtpUsername(string fromAddress)
    {
        var at = fromAddress.IndexOf('@');
        return at > 0 ? fromAddress[..at] : fromAddress;
    }
}

public enum UsageHeartbeatOutcome
{
    Sent,
    SkippedDisabled,
    SkippedSmtpNotConfigured,
    Failed
}

public sealed record UsageHeartbeatResult(UsageHeartbeatOutcome Outcome, string Message);

/// <summary>
/// 설치/사용 하트비트 메일. 앱 시작마다, 실패해도 업무를 막지 않습니다. 재고 데이터는 보내지 않습니다.
/// </summary>
public static class UsageHeartbeatService
{
    private static int _smtpMissingLogged;

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

            if (!options.IsSmtpConfigured())
            {
                if (Interlocked.Exchange(ref _smtpMissingLogged, 1) == 0)
                {
                    log?.Information(
                        "사용 알림 SMTP 미설정 — 발송 생략. usage-notify.json에 From/앱 비밀번호를 넣거나 내장 기본값을 확인하세요.");
                }

                return new UsageHeartbeatResult(
                    UsageHeartbeatOutcome.SkippedSmtpNotConfigured,
                    "SMTP가 설정되지 않아 발송하지 않았습니다.");
            }

            var mail = sender ?? new SmtpUsageMailSender();
            var subject = UsageNotifyMessage.BuildSubject(payload.InstallId);
            var body = UsageNotifyMessage.BuildBody(payload);
            mail.Send(options, subject, body);

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
