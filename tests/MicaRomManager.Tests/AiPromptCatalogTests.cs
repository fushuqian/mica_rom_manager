using MicaRomManager.Services.Ai;
using Xunit;

namespace MicaRomManager.Tests;

public class AiPromptCatalogTests
{
    [Fact]
    public void Definitions_CoverAllAiFeatures()
    {
        var ids = AiPromptCatalog.Definitions.Select(definition => definition.Id).ToList();

        Assert.Contains(AiPromptCatalog.AssistantId, ids);
        Assert.Contains("metadata", ids);
        Assert.Contains("tag", ids);
        Assert.Contains("translate", ids);
        Assert.Contains("query", ids);
        Assert.Contains("device", ids);
    }

    [Fact]
    public void Render_ReplacesKnownPlaceholdersAndKeepsUnknown()
    {
        var text = AiPromptCatalog.Render("标题：{{Title}} / {{Nope}}",
            new Dictionary<string, string> { ["Title"] = "银河战士" });

        Assert.Equal("标题：银河战士 / {{Nope}}", text);
    }

    [Fact]
    public void Defaults_ContainTheirPlaceholders()
    {
        Assert.Contains("{{FileName}}", AiPromptCatalog.Find("metadata").DefaultUser);
        Assert.Contains("{{GameCount}}", AiPromptCatalog.Find(AiPromptCatalog.AssistantId).DefaultSystem);
        Assert.Equal("", AiPromptCatalog.Find(AiPromptCatalog.AssistantId).DefaultUser);
    }

    [Fact]
    public void SystemFor_FallsBackToDefaultWithoutSettings()
    {
        Assert.Equal(AiPrompts.MetadataSystem, AiPromptCatalog.SystemFor("metadata"));
    }
}
