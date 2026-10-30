using System.IO;
using System.Text.Json;
using MicaRomManager.Models;
using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class EmulatorTemplateCatalogTests
{
    private static List<EmulatorTemplate> LoadBuiltInTemplates()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "EmulatorTemplates.json");
        Assert.True(File.Exists(path), "内置模板 JSON 没有复制到测试输出：" + path);

        var templates = JsonSerializer.Deserialize<List<EmulatorTemplate>>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(templates);
        return templates!;
    }

    [Fact]
    public void BuiltInTemplates_AreValidAndUnique()
    {
        var templates = LoadBuiltInTemplates();

        Assert.True(templates.Count >= 20, $"模板数量只有 {templates.Count} 个");
        Assert.Equal(templates.Count, templates
            .Select(template => template.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count());

        foreach (var template in templates)
        {
            Assert.False(string.IsNullOrWhiteSpace(template.Name));
            Assert.NotEmpty(template.Platforms);
            Assert.NotEmpty(template.Extensions);
            Assert.False(string.IsNullOrWhiteSpace(template.ArgsTemplate));
            Assert.Contains("{rom}", template.ArgsTemplate, StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(template.GithubRepo))
            {
                Assert.Equal(template.GithubRepo, EmulatorUpdater.NormalizeRepository(template.GithubRepo));
            }

            if (!string.IsNullOrWhiteSpace(template.WebsiteUrl))
            {
                Assert.True(Uri.TryCreate(template.WebsiteUrl, UriKind.Absolute, out _),
                    $"{template.Name} 的官网地址不合法：{template.WebsiteUrl}");
            }
        }
    }

    [Fact]
    public void BuiltInTemplates_UseWorkingUpdateSources()
    {
        var templates = LoadBuiltInTemplates();

        Assert.Contains(templates, template =>
            template.Name == "RPCS3" && template.GithubRepo == "RPCS3/rpcs3-binaries-win");
        Assert.Contains(templates, template =>
            template.Name == "DuckStation" && template.GithubRepo == "stenzek/duckstation");
        Assert.Contains(templates, template =>
            template.Name == "Mesen" && template.GithubRepo == "nesdev-org/MesenCE");
        Assert.Contains(templates, template =>
            template.Name == "Ymir" && template.GithubRepo == "ymir-emu/Ymir");

        // 没有 Windows Release 资产的目录改用官网手动更新
        foreach (var name in new[] { "RetroArch", "Dolphin", "Project64" })
        {
            var template = Assert.Single(templates, item => item.Name == name);
            Assert.True(string.IsNullOrWhiteSpace(template.GithubRepo), $"{name} 不应配置无 Windows 资产的仓库");
            Assert.False(string.IsNullOrWhiteSpace(template.WebsiteUrl), $"{name} 缺少官网地址");
        }
    }
}
