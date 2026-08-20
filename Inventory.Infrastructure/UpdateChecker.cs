using System.Net.Http.Headers;
using System.Text.Json;

namespace Inventory.Infrastructure;

public sealed record UpdateCheckResult(bool Checked, string Message, string FeedUrl);

public static class UpdateChecker
{
    public const string ReleasesUrl = "https://github.com/boam79/inventory_control/releases";
    public const string LatestApi = "https://api.github.com/repos/boam79/inventory_control/releases/latest";

    public static async Task<UpdateCheckResult> CheckAsync(HttpClient? client = null)
    {
        var owns = client is null;
        client ??= new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        try
        {
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SpringClinicInventory", "1.0"));
            using var response = await client.GetAsync(LatestApi);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(false, "업데이트를 확인하지 못했습니다. 재고 업무는 계속할 수 있습니다.", ReleasesUrl);
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var tag = doc.RootElement.TryGetProperty("tag_name", out var name) ? name.GetString() : "unknown";
            return new UpdateCheckResult(true, $"최신 배포: {tag}", ReleasesUrl);
        }
        catch
        {
            return new UpdateCheckResult(false, "업데이트 서버에 연결하지 못했습니다. 오프라인에서도 핵심 재고 기능은 동작합니다.", ReleasesUrl);
        }
        finally
        {
            if (owns)
            {
                client.Dispose();
            }
        }
    }
}
