using QQListener.Models;
using Xunit;

namespace QQListener.Tests.Models;

public class QqListenerDiagnosticsStateTests
{
    [Fact]
    public void RecordReadReportsUnavailableIdentitiesAsIgnored()
    {
        var diagnostics = new QqListenerDiagnosticsState();

        diagnostics.RecordRead(count: 3, unavailableAppIdentityCount: 2);

        Assert.Contains("忽略", diagnostics.LastReadSummary);
        Assert.DoesNotContain("文本识别", diagnostics.LastReadSummary);
        Assert.Contains("忽略", diagnostics.AppIdentityStatus);
        Assert.DoesNotContain("文本识别", diagnostics.AppIdentityStatus);
    }

    [Fact]
    public void EmptyFallbackReasonReportsUnknownIdentityAsIgnored()
    {
        var diagnostics = new QqListenerDiagnosticsState();

        diagnostics.RecordAppIdentityFallback("");

        Assert.Contains("忽略", diagnostics.AppIdentityStatus);
        Assert.DoesNotContain("文本识别", diagnostics.AppIdentityStatus);
    }

    [Fact]
    public void RecordErrorKeepsContextAndTypeWithoutExceptionSecrets()
    {
        const string context = "读取通知失败";
        var secrets = new[]
        {
            "secret-exception-message",
            "secret-sender",
            "secret-conversation",
            "private-body",
            @"C:\private\payload.json"
        };
        var exception = new InvalidOperationException(string.Join(" | ", secrets));
        var diagnostics = new QqListenerDiagnosticsState();

        diagnostics.RecordError(exception, context);

        Assert.Contains(context, diagnostics.LastErrorSummary, StringComparison.Ordinal);
        Assert.Contains(
            typeof(InvalidOperationException).FullName!,
            diagnostics.LastErrorSummary,
            StringComparison.Ordinal);
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, diagnostics.LastErrorSummary, StringComparison.Ordinal);
        }
    }
}
