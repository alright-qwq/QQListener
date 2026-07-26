using QQListener.Services;
using Xunit;

namespace QQListener.Tests.Services;

public class QqNotificationCandidateFilterTests
{
    [Theory]
    [InlineData("QQ", "", true)]
    [InlineData("腾讯QQ", "", true)]
    [InlineData("TIM", "", true)]
    [InlineData("", "Tencent.QQNT", true)]
    [InlineData("", "com.tencent.qq", true)]
    [InlineData("", "com.tencent.tim", true)]
    [InlineData("Tencent Meeting", "com.tencent.meeting", false)]
    [InlineData("Runtime Monitor", "runtime.monitor", false)]
    [InlineData("QQ音乐", "qqmusic", false)]
    public void RequiresTrustedIdentity(string appName, string appId, bool expected)
    {
        var snapshot = new WindowsToastSnapshot(1, appName, appId, true, ["sender", "body"]);
        Assert.Equal(expected, QqNotificationCandidateFilter.IsCandidate(snapshot, qqOnly: true));
    }

    [Theory]
    [InlineData("com.tencent.qqmusic")]
    [InlineData("com.tencent.qqbrowser")]
    [InlineData("Tencent.QQNTHelper")]
    [InlineData("prefix.Tencent.QQNT")]
    [InlineData("Tencent.QQNT.suffix")]
    [InlineData("not-com.tencent.qq")]
    public void RejectsLookalikeAppIds(string appId)
    {
        var snapshot = new WindowsToastSnapshot(1, "Untrusted App", appId, true, ["sender", "body"]);

        Assert.False(QqNotificationCandidateFilter.IsCandidate(snapshot, qqOnly: true));
    }

    [Fact]
    public void RejectsAnyShapeWhenAppIdentityIsUnavailable()
    {
        var snapshot = new WindowsToastSnapshot(1, "QQ", "Tencent.QQ", false, ["sender", "body"]);
        Assert.False(QqNotificationCandidateFilter.IsCandidate(snapshot, qqOnly: true));
        Assert.False(QqNotificationCandidateFilter.IsCandidate(snapshot, qqOnly: false));
    }

    [Fact]
    public void RejectsEmptyIdentityEvenWhenAppInfoIsAvailable()
    {
        var snapshot = new WindowsToastSnapshot(1, "", "", true, ["sender", "body"]);
        Assert.False(QqNotificationCandidateFilter.IsCandidate(snapshot, qqOnly: true));
        Assert.False(QqNotificationCandidateFilter.IsCandidate(snapshot, qqOnly: false));
    }

    [Fact]
    public void QqOnlyOffAllowsKnownNonQqIdentity()
    {
        var snapshot = new WindowsToastSnapshot(1, "Outlook", "Microsoft.Outlook", true, ["sender", "body"]);
        Assert.True(QqNotificationCandidateFilter.IsCandidate(snapshot, qqOnly: false));
    }
}
