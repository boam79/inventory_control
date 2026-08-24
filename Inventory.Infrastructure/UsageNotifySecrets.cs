using System.Security.Cryptography;
using System.Text;

namespace Inventory.Infrastructure;

/// <summary>
/// Windows DPAPI로 SMTP 비밀번호를 보호합니다. 비-Windows에서는 평문 필드를 사용합니다.
/// </summary>
public static class UsageNotifySecrets
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SpringClinicInventory.UsageNotify.v1");

    public static string Protect(string plain)
    {
        ArgumentException.ThrowIfNullOrEmpty(plain);
        if (!OperatingSystem.IsWindows())
        {
            return plain;
        }

        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plain),
            Entropy,
            DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public static bool TryUnprotect(string protectedOrPlain, out string? plain)
    {
        plain = null;
        if (string.IsNullOrWhiteSpace(protectedOrPlain))
        {
            return false;
        }

        if (!OperatingSystem.IsWindows())
        {
            plain = protectedOrPlain;
            return true;
        }

        try
        {
            var bytes = Convert.FromBase64String(protectedOrPlain);
            var raw = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
            plain = Encoding.UTF8.GetString(raw);
            return !string.IsNullOrEmpty(plain);
        }
        catch (FormatException)
        {
            // 예전 평문·비 Base64 값
            plain = protectedOrPlain;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
