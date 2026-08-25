using Inventory.Infrastructure;

namespace Inventory.Tests;

public class KillSwitchPolicyTests
{
    [Fact]
    public void Parse_valid_policy()
    {
        var json = """
            {
              "kill": false,
              "minVersion": "1.0.49",
              "message": "중지",
              "minVersionMessage": "구버전"
            }
            """;
        var policy = KillSwitchPolicyChecker.TryParse(json);
        Assert.NotNull(policy);
        Assert.False(policy!.Kill);
        Assert.Equal("1.0.49", policy.MinVersion);
        Assert.Equal("중지", policy.Message);
        Assert.Equal("구버전", policy.MinVersionMessage);
    }

    [Fact]
    public void Parse_defaults_empty_messages()
    {
        var policy = KillSwitchPolicyChecker.TryParse(
            """{"kill":true,"minVersion":"1.0.49","message":"","minVersionMessage":""}""");
        Assert.NotNull(policy);
        Assert.True(policy!.Kill);
        Assert.Equal(KillSwitchPolicyChecker.DefaultKillMessage, policy.Message);
        Assert.Equal(KillSwitchPolicyChecker.DefaultMinVersionMessage, policy.MinVersionMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"kill\":false}")]
    [InlineData("{\"kill\":false,\"minVersion\":\"\"}")]
    [InlineData("[]")]
    public void Parse_invalid_returns_null(string? json)
    {
        Assert.Null(KillSwitchPolicyChecker.TryParse(json));
    }

    [Theory]
    [InlineData("1.0.48", "1.0.49", true)]
    [InlineData("1.0.49", "1.0.49", false)]
    [InlineData("1.0.50", "1.0.49", false)]
    [InlineData("v1.0.48", "1.0.49", true)]
    [InlineData("1.0.49", "v1.0.49", false)]
    public void IsBelowMinVersion_compares(string current, string min, bool expected)
    {
        Assert.Equal(expected, KillSwitchPolicyChecker.IsBelowMinVersion(current, min));
    }

    [Fact]
    public void Evaluate_kill_takes_priority()
    {
        var policy = new KillSwitchPolicy(true, "9.9.9", "killed", "old");
        Assert.Equal(PolicyGateResult.Kill, KillSwitchPolicyChecker.Evaluate(policy, "1.0.49"));
    }

    [Fact]
    public void Evaluate_below_min_version()
    {
        var policy = new KillSwitchPolicy(false, "1.0.49", "killed", "old");
        Assert.Equal(
            PolicyGateResult.BelowMinVersion,
            KillSwitchPolicyChecker.Evaluate(policy, "1.0.48"));
    }

    [Fact]
    public void Evaluate_allow_at_or_above_min()
    {
        var policy = new KillSwitchPolicy(false, "1.0.49", "killed", "old");
        Assert.Equal(PolicyGateResult.Allow, KillSwitchPolicyChecker.Evaluate(policy, "1.0.49"));
        Assert.Equal(PolicyGateResult.Allow, KillSwitchPolicyChecker.Evaluate(policy, "1.0.50"));
    }

    [Fact]
    public void BlockMessage_uses_policy_text()
    {
        var policy = new KillSwitchPolicy(true, "1.0.49", "킬 메시지", "최소 메시지");
        Assert.Equal("킬 메시지", KillSwitchPolicyChecker.BlockMessage(PolicyGateResult.Kill, policy));
        Assert.Equal(
            "최소 메시지",
            KillSwitchPolicyChecker.BlockMessage(PolicyGateResult.BelowMinVersion, policy));
    }

    [Fact]
    public async Task CheckAsync_fetch_failed_is_fail_open_signal()
    {
        using var client = new HttpClient(new FailHandler()) { Timeout = TimeSpan.FromSeconds(2) };
        var check = await KillSwitchPolicyChecker.CheckAsync(
            "1.0.49",
            "https://example.invalid/policy.json",
            client);
        Assert.Equal(PolicyGateResult.FetchFailed, check.Result);
        Assert.Null(check.Policy);
        Assert.False(string.IsNullOrWhiteSpace(check.ErrorMessage));
    }

    [Fact]
    public async Task CheckAsync_kill_true_blocks()
    {
        var json = """{"kill":true,"minVersion":"1.0.49","message":"중지됨","minVersionMessage":"구버전"}""";
        using var client = new HttpClient(new FixedJsonHandler(json));
        var check = await KillSwitchPolicyChecker.CheckAsync("1.0.49", "https://example.test/p.json", client);
        Assert.Equal(PolicyGateResult.Kill, check.Result);
        Assert.Equal("중지됨", KillSwitchPolicyChecker.BlockMessage(check.Result, check.Policy));
    }

    private sealed class FailHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("simulated offline");
    }

    private sealed class FixedJsonHandler : HttpMessageHandler
    {
        private readonly string _json;

        public FixedJsonHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_json)
            });
    }
}
