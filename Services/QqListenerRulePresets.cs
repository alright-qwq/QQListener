using QQListener.Models;

namespace QQListener.Services;

public static class QqListenerRulePresets
{
    public static void Apply(QqListenerSettings settings, QqListenerRulePreset preset)
    {
        ArgumentNullException.ThrowIfNull(settings);

        switch (preset)
        {
            case QqListenerRulePreset.HomeworkReminder:
                settings.ImportantPersonsText = "";
                settings.ImportantKeywordsText = "作业\n提交\n截止\n收作业";
                settings.CallingEnabled = false;
                settings.CallingKeyword = "";
                return;
            case QqListenerRulePreset.TeacherCalling:
                settings.ImportantPersonsText = "老师\n班主任";
                settings.ImportantKeywordsText = "";
                settings.CallingEnabled = true;
                settings.CallingKeyword = "呼叫";
                return;
            case QqListenerRulePreset.ExamNotice:
                settings.ImportantPersonsText = "";
                settings.ImportantKeywordsText = "考试\n监考\n考场\n通知";
                settings.CallingEnabled = false;
                settings.CallingKeyword = "";
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(preset), preset, "未知规则预设。");
        }
    }
}
