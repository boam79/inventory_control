using System.Net.Http.Headers;
using System.Text.Json;

namespace Inventory.Infrastructure;

/// <summary>GitHub raw 정책 파일(policy/kill-switch.json) 스키마.</summary>
public sealed record KillSwitchPolicy(
    bool Kill,
    string MinVersion,
    string Message,
    string MinVersionMessage);

/// <summary>원격 정책 평가 결과. FetchFailed는 호출측에서 fail-open(허용) 처리.</summary>
public enum PolicyGateResult
{
    Allow,
    Kill,
    BelowMinVersion,
    FetchFailed
}

public sealed record PolicyGateCheck(
    PolicyGateResult Result,
    KillSwitchPolicy? Policy,
    string? ErrorMessage);

/// <summary>
/// 킬스위치·최소 버전 정책. 별도 서버 없음 — GitHub main 브랜치 raw JSON.
/// 설정 UI에는 노출하지 않음. 토글은 개발자가 저장소 파일을 편집.
/// </summary>
public static class KillSwitchPolicyChecker
{
    public const string DefaultPolicyUrl =
        "https://raw.githubusercontent.com/boam79/inventory_control/main/policy/kill-switch.json";

    public const string DefaultKillMessage =
        "사용이 중지되었습니다. 개발자에게 문의하세요.";

    public const string DefaultMinVersionMessage =
        "구버전은 사용할 수 없습니다. 프로그램을 다시 실행해 업데이트를 완료하세요.";

    public static KillSwitchPolicy? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var kill = root.TryGetProperty("kill", out var killEl)
                       && killEl.ValueKind is JsonValueKind.True or JsonValueKind.False
                       && killEl.GetBoolean();

            var minVersion = root.TryGetProperty("minVersion", out var minEl)
                             && minEl.ValueKind == JsonValueKind.String
                ? (minEl.GetString() ?? "").Trim()
                : "";

            if (string.IsNullOrWhiteSpace(minVersion))
            {
                return null;
            }

            var message = root.TryGetProperty("message", out var msgEl)
                          && msgEl.ValueKind == JsonValueKind.String
                ? (msgEl.GetString() ?? "").Trim()
                : "";
            if (string.IsNullOrWhiteSpace(message))
            {
                message = DefaultKillMessage;
            }

            var minMessage = root.TryGetProperty("minVersionMessage", out var minMsgEl)
                             && minMsgEl.ValueKind == JsonValueKind.String
                ? (minMsgEl.GetString() ?? "").Trim()
                : "";
            if (string.IsNullOrWhiteSpace(minMessage))
            {
                minMessage = DefaultMinVersionMessage;
            }

            return new KillSwitchPolicy(kill, minVersion, message, minMessage);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>현재 버전이 minVersion 미만이면 true (minVersion &gt; current).</summary>
    public static bool IsBelowMinVersion(string currentVersion, string? minVersion) =>
        UpdateChecker.IsRemoteNewer(minVersion, currentVersion);

    public static PolicyGateResult Evaluate(KillSwitchPolicy policy, string currentVersion)
    {
        if (policy.Kill)
        {
            return PolicyGateResult.Kill;
        }

        if (IsBelowMinVersion(currentVersion, policy.MinVersion))
        {
            return PolicyGateResult.BelowMinVersion;
        }

        return PolicyGateResult.Allow;
    }

    public static string BlockMessage(PolicyGateResult result, KillSwitchPolicy? policy) =>
        result switch
        {
            PolicyGateResult.Kill => policy?.Message ?? DefaultKillMessage,
            PolicyGateResult.BelowMinVersion => policy?.MinVersionMessage ?? DefaultMinVersionMessage,
            _ => ""
        };

    /// <summary>
    /// 정책 fetch. 네트워크·파싱 실패 시 FetchFailed (호출측 fail-open).
    /// </summary>
    public static async Task<PolicyGateCheck> CheckAsync(
        string currentVersion,
        string? policyUrl = null,
        HttpClient? client = null,
        CancellationToken cancellationToken = default)
    {
        var owns = client is null;
        client ??= new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        var url = string.IsNullOrWhiteSpace(policyUrl) ? DefaultPolicyUrl : policyUrl.Trim();
        try
        {
            EnsureUserAgent(client);
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new PolicyGateCheck(
                    PolicyGateResult.FetchFailed,
                    null,
                    $"정책 HTTP {(int)response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var policy = TryParse(json);
            if (policy is null)
            {
                return new PolicyGateCheck(PolicyGateResult.FetchFailed, null, "정책 JSON 파싱 실패");
            }

            return new PolicyGateCheck(Evaluate(policy, currentVersion), policy, null);
        }
        catch (Exception ex)
        {
            return new PolicyGateCheck(PolicyGateResult.FetchFailed, null, ex.Message);
        }
        finally
        {
            if (owns)
            {
                client.Dispose();
            }
        }
    }

    private static void EnsureUserAgent(HttpClient client)
    {
        if (client.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("SpringClinicInventory", "1.0"));
        }
    }
}
