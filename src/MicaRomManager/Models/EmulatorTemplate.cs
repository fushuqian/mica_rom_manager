namespace MicaRomManager.Models;

public sealed class EmulatorTemplate
{
    public string Name { get; set; } = "";
    public List<string> Platforms { get; set; } = new();
    public List<string> Extensions { get; set; } = new();
    public string ArgsTemplate { get; set; } = "\"{rom}\"";
    public string CorePath { get; set; } = "";
    public string GithubRepo { get; set; } = "";
    /// <summary>模板自带官网/下载页；为空时按 GitHub 仓库生成 releases 地址。</summary>
    public string WebsiteUrl { get; set; } = "";
    public string Notes { get; set; } = "";

    public string DisplayName => string.IsNullOrWhiteSpace(GithubRepo)
        ? Name
        : $"{Name}（{GithubRepo}）";
}
