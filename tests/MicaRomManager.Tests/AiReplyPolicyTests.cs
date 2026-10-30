using MicaRomManager.Services.Ai;
using Xunit;

namespace MicaRomManager.Tests;

public class AiReplyPolicyTests
{
    private readonly AiReplyPolicy _policy = new();

    [Theory]
    [InlineData("库内年份可能有误，要我改正吗？", true)]
    [InlineData("需要我帮你整理一下吗?", true)]
    [InlineData("这是《银河战士》的介绍。", false)]
    [InlineData("要我改正吗", false)]          // 没有问号不算征求意见
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPermissionAsk_DetectsTrailingPermissionQuestions(string? text, bool expected)
    {
        Assert.Equal(expected, _policy.IsPermissionAsk(text));
    }

    [Fact]
    public void StripTrailingPermissionAsk_KeepsAnalysisAndDropsQuestion()
    {
        // 截断点所在的那句标点也一起去掉，只留前面的分析内容
        Assert.Equal("年份 2009 有误", _policy.StripTrailingPermissionAsk("年份 2009 有误。要我改正吗？"));
    }

    [Fact]
    public void StripTrailingPermissionAsk_ReturnsEmptyWhenOnlyTheQuestion()
    {
        Assert.Equal("", _policy.StripTrailingPermissionAsk("要我改正吗？"));
    }

    [Fact]
    public void MergePreface_JoinsKeptTextAndNewContent()
    {
        Assert.Equal("正文", _policy.MergePreface("", "正文"));
        Assert.Equal("第一段\n\n第二段", _policy.MergePreface("第一段。要我改吗？", "第二段"));
        Assert.Equal("第二段", _policy.MergePreface("要我改吗？", "第二段"));
    }

    [Theory]
    [InlineData("介绍一下银河战士", true)]
    [InlineData("这游戏怎么样", true)]
    [InlineData("删除这个游戏", false)]
    [InlineData("", false)]
    public void IsIntroRequest_DetectsIntroQuestions(string text, bool expected)
    {
        Assert.Equal(expected, _policy.IsIntroRequest(text));
    }

    [Theory]
    [InlineData(false, "", "", "介绍一下银河战士", true)]     // 正文空 + 介绍类提问
    [InlineData(false, "", "太短了。", "介绍一下银河战士", true)]
    [InlineData(false, "", "这段介绍足够长，超过了三十个字符的阈值，应当被当作正文，不能再拦卡片了。", "介绍一下银河战士", false)]
    [InlineData(false, "已有正文", "", "介绍一下银河战士", false)]  // 已经有 preface 不再拦
    [InlineData(true, "", "", "介绍一下银河战士", false)]          // 本轮已拦过一次
    [InlineData(false, "", "", "把评分改成五星", false)]           // 不是介绍类提问
    public void ShouldRequestIntroFirst_OnlyGuardsShortIntroReplies(
        bool alreadyGuarded, string preface, string content, string userText, bool expected)
    {
        Assert.Equal(expected, _policy.ShouldRequestIntroFirst(alreadyGuarded, preface, content, userText));
    }

    [Fact]
    public void MinIntroBodyLength_IsConfigurable()
    {
        const string body = "这段介绍正文已经超过了三十个字符的阈值，所以默认策略不会拦下卡片，但把阈值调到一百仍然会拦。";

        var strict = new AiReplyPolicy { MinIntroBodyLength = 100 };

        Assert.True(strict.ShouldRequestIntroFirst(false, "", body, "介绍一下"));
        Assert.False(_policy.ShouldRequestIntroFirst(false, "", body, "介绍一下"));
    }
}
