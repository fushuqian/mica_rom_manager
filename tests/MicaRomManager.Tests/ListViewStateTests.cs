using System.Text.Json;
using MicaRomManager.Models;
using Xunit;

namespace MicaRomManager.Tests;

public class ListViewStateTests
{
    [Fact]
    public void Json_RoundTripsHiddenColumns()
    {
        var state = new ListViewState
        {
            Search = "mario",
            ColumnWidths = new Dictionary<string, double> { ["标题"] = 220 },
            HiddenColumns = new List<string> { "指纹", "标签" },
        };

        var json = JsonSerializer.Serialize(state);
        var restored = JsonSerializer.Deserialize<ListViewState>(json);

        Assert.NotNull(restored);
        Assert.Equal(new[] { "指纹", "标签" }, restored!.HiddenColumns);
        Assert.Equal(220, restored.ColumnWidths["标题"]);
    }

    [Fact]
    public void Json_MissingHiddenColumnsFallsBackToEmpty()
    {
        const string oldJson = "{\"Search\":\"x\",\"ColumnWidths\":{\"标题\":220}}";

        var restored = JsonSerializer.Deserialize<ListViewState>(oldJson);

        Assert.NotNull(restored);
        Assert.Empty(restored!.HiddenColumns);
    }

    [Fact]
    public void PruneHidden_DropsUnknownAndKeepsOrder()
    {
        var state = new ListViewState { HiddenColumns = new List<string> { "指纹", "已删除的列", "标签", "指纹" } };

        state.PruneHidden(new[] { "★", "标题", "标签", "指纹" });

        Assert.Equal(new[] { "指纹", "标签" }, state.HiddenColumns);
    }

    [Fact]
    public void PruneHidden_ClearsWhenEverythingHidden()
    {
        var state = new ListViewState { HiddenColumns = new List<string> { "★", "标题" } };

        state.PruneHidden(new[] { "★", "标题" });

        Assert.Empty(state.HiddenColumns);
    }

    [Fact]
    public void PruneHidden_IgnoresBlankNamesAndEmptyAvailableList()
    {
        var state = new ListViewState { HiddenColumns = new List<string> { "", "  ", "标签" } };

        state.PruneHidden(Array.Empty<string>());

        Assert.Empty(state.HiddenColumns);
    }
}
