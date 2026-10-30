using System.Security.Cryptography;
using System.Text;

namespace MicaRomManager.Services;

/// <summary>
/// 本地敏感值加密（如 AI API Key）：用 Windows DPAPI 按当前用户加密，密文带 <c>dpapi:</c> 前缀。
/// 没有前缀的值按旧明文处理，保证老库可以直接升级；换机器/换用户解不开时返回空，让用户重填。
/// </summary>
public static class SecretProtector
{
    private const string Prefix = "dpapi:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MicaRomManager.secret.v1");

    public static bool IsProtected(string? value) =>
        value?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    /// <summary>加密；空值返回空，已加密的原样返回，加密不可用时退回明文（不丢配置）。</summary>
    public static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        if (IsProtected(plain)) return plain;

        try
        {
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(bytes);
        }
        catch
        {
            return plain;
        }
    }

    /// <summary>解密；旧明文原样返回，密文损坏或换用户后返回空。</summary>
    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        if (!IsProtected(stored)) return stored;

        try
        {
            var bytes = Convert.FromBase64String(stored[Prefix.Length..]);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser));
        }
        catch
        {
            return "";
        }
    }

    /// <summary>把库里遗留的明文密钥就地加密（幂等；启动时调一次）。</summary>
    public static void Migrate(Data.SettingsRepository settings, string key)
    {
        var current = settings.Get(key);
        if (string.IsNullOrEmpty(current) || IsProtected(current)) return;

        var protectedValue = Protect(current);
        if (IsProtected(protectedValue)) settings.Set(key, protectedValue);
    }
}
