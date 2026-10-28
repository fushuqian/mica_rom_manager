namespace MicaRomManager.Models;

public sealed class EmulatorTemplate
{
    public string Name { get; set; } = "";
    public List<string> Platforms { get; set; } = new();
    public List<string> Extensions { get; set; } = new();
    public string ArgsTemplate { get; set; } = "\"{rom}\"";
    public string CorePath { get; set; } = "";
    public string GithubRepo { get; set; } = "";
    public string Notes { get; set; } = "";

    public string DisplayName => string.IsNullOrWhiteSpace(GithubRepo)
        ? Name
        : $"{Name}（{GithubRepo}）";
}
