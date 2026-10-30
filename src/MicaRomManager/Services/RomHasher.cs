using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MicaRomManager.Services;

/// <summary>
/// ROM 内容指纹：小文件全量 SHA256，大文件用「大小 + 首/中/尾采样」的 SHA256，
/// 采样结果带 fast: 前缀，避免几 GB 镜像每次都被完整读取。
/// </summary>
public static class RomHasher
{
    private const long FullHashLimit = 128L * 1024 * 1024;
    private const int SampleSize = 1024 * 1024;

    public static string Compute(string path) => Compute(path, FullHashLimit);

    /// <summary>阈值可注入，便于测试采样分支（生产统一走 <see cref="FullHashLimit"/>）。</summary>
    internal static string Compute(string path, long fullHashLimit)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return "";

            if (info.Length <= fullHashLimit) return HashFull(path);
            return "fast:" + HashSampled(path, info.Length);
        }
        catch
        {
            return "";
        }
    }

    private static string HashFull(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string HashSampled(string path, long length)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024);
        using var sha = SHA256.Create();

        var buffer = new byte[SampleSize];
        var sizeBytes = BitConverter.GetBytes(length);
        sha.TransformBlock(sizeBytes, 0, sizeBytes.Length, null, 0);

        foreach (var offset in new[] { 0L, Math.Max(0, length / 2 - SampleSize / 2), Math.Max(0, length - SampleSize) })
        {
            stream.Seek(offset, SeekOrigin.Begin);
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read > 0) sha.TransformBlock(buffer, 0, read, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }
}
