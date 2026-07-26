using QQListener.Models;
using QQListener.Services;
using Xunit;

namespace QQListener.Tests.Services;

public class QqToastMessageParserTests
{
    [Theory]
    [InlineData("Alice: hello", QqToastParseConfidence.Known, "已解析 QQ Toast")]
    [InlineData("only body", QqToastParseConfidence.Degraded, "未知格式，已降级解析")]
    public void ReportsConfidenceForSingleLine(
        string text,
        QqToastParseConfidence expected,
        string reason)
    {
        var ok = new QqToastMessageParser().TryParse(
            [text], DateTimeOffset.Parse("2026-07-26T12:00:00+08:00"),
            out var message, out var actualReason, out var confidence);

        Assert.True(ok);
        Assert.NotNull(message);
        Assert.Equal(expected, confidence);
        Assert.Equal(reason, actualReason);
    }

    [Fact]
    public void ReportsKnownForGroupLayout()
    {
        var ok = new QqToastMessageParser().TryParse(
            ["班群", "Alice", "hello"], DateTimeOffset.Now,
            out _, out var reason, out var confidence);

        Assert.True(ok);
        Assert.Equal(QqToastParseConfidence.Known, confidence);
        Assert.Equal("已解析 QQ Toast", reason);
    }

    [Fact]
    public void ReportsDegradedForTwoLinesWithoutSeparator()
    {
        var ok = new QqToastMessageParser().TryParse(
            ["Alice", "hello"], DateTimeOffset.Now,
            out var message, out var reason, out var confidence);

        Assert.True(ok);
        Assert.NotNull(message);
        Assert.Equal(QqToastParseConfidence.Degraded, confidence);
        Assert.Equal("未知格式，已降级解析", reason);
    }

    [Fact]
    public void EmptyToastStillFails()
    {
        var ok = new QqToastMessageParser().TryParse(
            ["QQ", "  "], DateTimeOffset.Now,
            out var message, out var reason, out var confidence);

        Assert.False(ok);
        Assert.Null(message);
        Assert.Equal("通知没有可解析文本", reason);
        Assert.Equal(QqToastParseConfidence.Degraded, confidence);
    }
}
