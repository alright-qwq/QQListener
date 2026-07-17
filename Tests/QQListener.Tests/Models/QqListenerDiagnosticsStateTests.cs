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
}
