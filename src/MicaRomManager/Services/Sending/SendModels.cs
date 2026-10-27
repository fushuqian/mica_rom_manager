namespace MicaRomManager.Services.Sending;

public enum ConflictPolicy
{
    Skip,
    Overwrite,
}

/// <summary>一个待发送的 ROM 文件（多碟游戏每碟一条）。</summary>
public sealed record SendFile(string SourcePath, string Platform, string Title, int? Year);

public sealed record SendProgress(int Index, int Total, string FileName);

public sealed record SendResult(
    int Copied,
    int Skipped,
    int Failed,
    long BytesCopied,
    string TargetRoot,
    List<string> Warnings);
