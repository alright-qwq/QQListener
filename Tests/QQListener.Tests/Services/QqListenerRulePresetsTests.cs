using System.Text.Json;
using QQListener.Models;
using QQListener.Services;
using Xunit;

namespace QQListener.Tests.Services;

public sealed class QqListenerRulePresetsTests
{
    [Theory]
    [InlineData(QqListenerRulePreset.HomeworkReminder, "作业\n提交\n截止\n收作业", "", false)]
    [InlineData(QqListenerRulePreset.TeacherCalling, "", "老师\n班主任", true)]
    [InlineData(QqListenerRulePreset.ExamNotice, "考试\n监考\n考场\n通知", "", false)]
    public void ApplyReplacesOnlyPresetRuleFields(
        QqListenerRulePreset preset, string keywords, string persons, bool calling)
    {
        var settings = SentinelSettings();

        QqListenerRulePresets.Apply(settings, preset);
        var once = JsonSerializer.Serialize(settings);
        QqListenerRulePresets.Apply(settings, preset);

        Assert.Equal(once, JsonSerializer.Serialize(settings));
        Assert.Equal(keywords, settings.ImportantKeywordsText);
        Assert.Equal(persons, settings.ImportantPersonsText);
        Assert.Equal(calling, settings.CallingEnabled);
        Assert.Equal("qq.toast", settings.AllowedSourcesText);
        Assert.Equal("22:00", settings.RuleTimeWindowStartText);
        Assert.True(settings.IsEnabled);
    }

    [Fact]
    public void ApplyRejectsUnknownPreset()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QqListenerRulePresets.Apply(SentinelSettings(), (QqListenerRulePreset)999));
    }

    private static QqListenerSettings SentinelSettings() => new()
    {
        IsEnabled = true,
        AllowedSourcesText = "qq.toast",
        RuleTimeWindowEnabled = true,
        RuleTimeWindowStartText = "22:00",
        RuleTimeWindowEndText = "07:00",
        BlacklistText = "保留黑名单",
        SomeoneAtMe = true,
        ImportantPersonsText = "旧人物",
        ImportantKeywordsText = "旧关键词",
        CallingEnabled = true,
        CallingKeyword = "旧呼叫词"
    };
}
