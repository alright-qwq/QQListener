# QQListener 全量 OKR 完成实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不放宽 NT QQ 应用身份过滤的前提下，将 QQListener 交付为 1.1.0.0，补齐格式诊断、分级规则、规则预览、自动化、组件隐私和干净安装发布门禁。

**Architecture:** 保持 `Reader -> Parser -> Evaluator -> Pipeline -> Worker -> Notifier` 主链；预览服务只复用 Parser/Evaluator，自动化 Hub 只接收 Worker 的正式接受事件，组件 presenter 只读取 `LatestMessageState`。规则仍为固定优先级的扁平设置，不引入通用 DSL；发布验证器在受控临时目录内安全解包并验证必需文件与版本。

**Tech Stack:** C# 12、.NET 8 Windows、Avalonia、ClassIsland Plugin SDK、CommunityToolkit.Mvvm、xUnit、YamlDotNet、GitHub Actions、PowerShell。

---

## 文件职责图

- `Models/QqToastParseConfidence.cs`：区分已知与降级 Toast 格式。
- `Models/QqListenerSettings.cs`：来源、允许时间段和分级置顶持久化设置。
- `Models/MessageRulePreviewResult.cs`：无副作用规则预览结果。
- `Models/QqListenerRulePreset.cs`：三个稳定的官方预设标识。
- `Services/QqToastMessageParser.cs`：解析并报告置信度，不改变 `MessageEvent` 字段。
- `Services/MessageRuleEvaluator.cs`：按固定顺序评估黑名单、来源、时间、呼叫、人物、关键词和 @我。
- `Services/MessageRulePreviewService.cs`：模拟文本经 Parser/Evaluator 的只读预览。
- `Services/QqListenerRulePresets.cs`：幂等替换人物、关键词和呼叫规则。
- `Services/MessageAutomationEventHub.cs`：隔离订阅者异常的正式消息事件总线。
- `Automations/Triggers/*.cs`：普通、重要、呼叫三个无设置触发器。
- `Automations/Actions/ClearLatestMessageAction.cs`：清空最后消息。
- `Components/LatestQqMessagePresenter.cs`：可测试的等级过滤与脱敏展示决策。
- `Views/SettingsPages/QqListenerSettingsPage.*`：设置、预设和规则测试入口。
- `Tools/QQListener.ReleaseVerifier/ReleaseMetadataVerifier.cs`：安全解包、必需文件、程序集和版本校验。
- `docs/release/1.1.0.0-verification.md`：本次干净安装和宿主冒烟的真实记录。

### Task 1: Toast 置信度与可见诊断

**Files:**
- Create: `Models/QqToastParseConfidence.cs`
- Create: `Tests/QQListener.Tests/Services/QqToastMessageParserTests.cs`
- Modify: `Services/QqToastMessageParser.cs`
- Modify: `Services/QqNotificationPoller.cs`
- Modify: `Models/QqListenerDiagnosticsState.cs`
- Modify: `Views/SettingsPages/QqListenerSettingsPage.axaml`
- Modify: `Tests/QQListener.Tests/Services/QqNotificationPollerTests.cs`
- Modify: `Tests/QQListener.Tests/Services/PrivacyTests.cs`

- [ ] **Step 1: 写解析置信度失败测试**

新增测试，明确单行带分隔符和三行群聊为 `Known`，单行正文及两行兜底为 `Degraded`，空文本仍失败：

```csharp
[Theory]
[InlineData("Alice: hello", QqToastParseConfidence.Known, "已解析 QQ Toast")]
[InlineData("only body", QqToastParseConfidence.Degraded, "未知格式，已降级解析")]
public void ReportsConfidenceForSingleLine(string text, QqToastParseConfidence expected, string reason)
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
```

- [ ] **Step 2: 运行测试并确认失败**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter "FullyQualifiedName~QqToastMessageParserTests"`

Expected: FAIL，编译器报告 `QqToastParseConfidence` 或五参数 `TryParse` 不存在。

- [ ] **Step 3: 实现置信度且保留兼容重载**

新增枚举：

```csharp
namespace QQListener.Models;

public enum QqToastParseConfidence
{
    Degraded = 0,
    Known = 1
}
```

在 Parser 中保留现有四参数方法并转发到新重载；让 `ParseSingleLine`、`ParseMultiLine` 返回是否命中已知布局：

```csharp
public bool TryParse(
    IReadOnlyList<string> texts,
    DateTimeOffset receivedAt,
    out MessageEvent? messageEvent,
    out string reason) =>
    TryParse(texts, receivedAt, out messageEvent, out reason, out _);

public bool TryParse(
    IReadOnlyList<string> texts,
    DateTimeOffset receivedAt,
    out MessageEvent? messageEvent,
    out string reason,
    out QqToastParseConfidence confidence)
{
    var rawText = NormalizeTexts(texts);
    var contentLines = RemoveNoiseLines(rawText);
    confidence = QqToastParseConfidence.Degraded;
    if (contentLines.Length == 0)
    {
        messageEvent = null;
        reason = "通知没有可解析文本";
        return false;
    }

    var sender = SourceDisplayName;
    var conversation = "";
    var body = "";
    var known = contentLines.Length == 1
        ? ParseSingleLine(contentLines[0], ref sender, ref conversation, ref body)
        : ParseMultiLine(contentLines, ref sender, ref conversation, ref body);
    confidence = known ? QqToastParseConfidence.Known : QqToastParseConfidence.Degraded;
    messageEvent = new MessageEvent(
        SourceId, SourceDisplayName, sender, conversation, body, receivedAt,
        MessageImportance.Normal, rawText, CreateDeduplicationKey(SourceId, contentLines));
    reason = known ? "已解析 QQ Toast" : "未知格式，已降级解析";
    return true;
}
```

单行只有成功拆出“发送者：正文”才返回 `true`；多行最后一行能拆分或至少三行时返回 `true`，恰好两行兜底返回 `false`。

- [ ] **Step 4: 增加诊断和隐私测试**

在 Poller 测试中先用空读取建立基线，再送入 `only private-body`，断言事件仍接受且 `LastFormatSummary` 为固定脱敏文案；Privacy 测试把该字段加入脱敏集合：

```csharp
[Fact]
public async Task DegradedToastIsAcceptedAndRecordedWithoutContent()
{
    var diagnostics = new QqListenerDiagnosticsState();
    var reader = SequencedNotificationReader.Create(
        Success(), Success(Snapshot(1, "private-body")));
    var poller = CreatePoller(reader, diagnostics);
    await poller.PollAsync(false, default);
    var result = await poller.PollAsync(false, default);
    Assert.Single(result.AcceptedEvents);
    Assert.Equal("未知格式，已降级解析", diagnostics.LastFormatSummary);
    Assert.DoesNotContain("private-body", diagnostics.LastFormatSummary);
}
```

- [ ] **Step 5: 让 Poller 写入独立可见诊断**

在 `QqListenerDiagnosticsState` 增加初值为“暂无”的 `LastFormatSummary` 和 `RecordFormatWarning(string reason)`；Poller 调用五参数重载，遇到 `Degraded` 时调用该方法并记录不含正文、发送者和规则值的结构化 Warning：

```csharp
if (confidence == QqToastParseConfidence.Degraded)
{
    diagnostics.RecordFormatWarning(parseReason);
    logger.LogWarning("QQ Toast 格式未知，已使用降级解析。TextLines={TextLines}", snapshot.Texts.Count);
}
```

在设置页诊断区增加绑定 `Diagnostics.LastFormatSummary` 的“格式诊断”项。

- [ ] **Step 6: 验证任务并提交**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter "FullyQualifiedName~QqToastMessageParserTests|FullyQualifiedName~QqNotificationPollerTests|FullyQualifiedName~PrivacyTests"`

Expected: PASS，所有所选测试通过。

```powershell
git add Models/QqToastParseConfidence.cs Services/QqToastMessageParser.cs Services/QqNotificationPoller.cs Models/QqListenerDiagnosticsState.cs Views/SettingsPages/QqListenerSettingsPage.axaml Tests/QQListener.Tests/Services
git commit -m "feat: 增加未知通知格式诊断"
```

### Task 2: 来源、跨午夜时间规则与分级置顶

**Files:**
- Create: `Tests/QQListener.Tests/Services/MessageRuleEvaluatorTests.cs`
- Modify: `Models/QqListenerSettings.cs`
- Modify: `Services/MessageRuleEvaluator.cs`
- Modify: `Services/ClassIslandMessageNotifier.cs`
- Modify: `Views/SettingsPages/QqListenerSettingsPage.axaml`
- Modify: `Tests/QQListener.Tests/Models/QqListenerSettingsTests.cs`
- Modify: `Tests/QQListener.Tests/Services/ClassIslandMessageNotifierTests.cs`
- Modify: `Tests/QQListener.Tests/Services/PrivacyTests.cs`

- [ ] **Step 1: 写设置默认值、规范化和规则顺序失败测试**

测试以下不可变验收：空来源列表允许全部；来源精确匹配且忽略大小写；黑名单优先于来源和时间；普通窗口起点包含终点不含；跨午夜窗口正确；起止相等为全天；原因不回显私有配置。

```csharp
[Fact]
public void FixedPriorityIsBlacklistThenSourceThenTimeThenImportance()
{
    var settings = new QqListenerSettings
    {
        BlacklistText = "secret-blacklist",
        AllowedSourcesText = "allowed.source",
        RuleTimeWindowEnabled = true,
        RuleTimeWindowStartText = "08:00",
        RuleTimeWindowEndText = "18:00",
        CallingEnabled = true,
        CallingKeyword = "call"
    };
    var evaluator = new MessageRuleEvaluator(settings);
    Assert.Equal("命中黑名单规则", evaluator.Evaluate(Event("other", "secret-blacklist call", "12:00")).Reason);
    Assert.Equal("来源不在允许列表", evaluator.Evaluate(Event("other", "call", "12:00")).Reason);
    Assert.Equal("不在允许时间段", evaluator.Evaluate(Event("allowed.source", "call", "20:00")).Reason);
    Assert.Equal(MessageImportance.Calling, evaluator.Evaluate(Event("allowed.source", "call", "12:00")).Event.Importance);
}

[Theory]
[InlineData("08:00", true)]
[InlineData("17:59", true)]
[InlineData("18:00", false)]
public void DayWindowUsesInclusiveStartExclusiveEnd(string clock, bool accepted)
{
    Assert.Equal(accepted, !EvaluateAt("08:00", "18:00", clock).IsIgnored);
}

[Theory]
[InlineData("22:00", true)]
[InlineData("06:59", true)]
[InlineData("07:00", false)]
[InlineData("12:00", false)]
public void OvernightWindowWrapsMidnight(string clock, bool accepted)
{
    Assert.Equal(accepted, !EvaluateAt("22:00", "07:00", clock).IsIgnored);
}

private static MessageEvent Event(string source, string body, string clock)
{
    var time = TimeOnly.ParseExact(clock, "HH:mm", CultureInfo.InvariantCulture);
    return new MessageEvent(
        source, "QQ", "Alice", "班群", body,
        new DateTimeOffset(2026, 7, 26, time.Hour, time.Minute, 0, TimeSpan.FromHours(8)),
        MessageImportance.Normal, ["Alice", body], $"{source}:{clock}:{body}");
}

private static MessageRuleEvaluationResult EvaluateAt(string start, string end, string clock)
{
    var settings = new QqListenerSettings
    {
        RuleTimeWindowEnabled = true,
        RuleTimeWindowStartText = start,
        RuleTimeWindowEndText = end
    };
    settings.Normalize();
    return new MessageRuleEvaluator(settings).Evaluate(Event("qq.toast", "normal", clock));
}
```

- [ ] **Step 2: 运行规则测试并确认失败**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter "FullyQualifiedName~MessageRuleEvaluatorTests|FullyQualifiedName~QqListenerSettingsTests"`

Expected: FAIL，新增设置属性不存在。

- [ ] **Step 3: 增加设置字段及时间规范化**

在 `QqListenerSettings` 增加以下字段和派生列表：

```csharp
[ObservableProperty] private string _allowedSourcesText = "";
[ObservableProperty] private bool _ruleTimeWindowEnabled;
[ObservableProperty] private string _ruleTimeWindowStartText = "00:00";
[ObservableProperty] private string _ruleTimeWindowEndText = "00:00";
[ObservableProperty] private bool _normalTopmost;
[ObservableProperty] private bool _importantTopmost;
[ObservableProperty] private bool _callingTopmost = true;

[JsonIgnore]
public IReadOnlyList<string> AllowedSources => SplitLines(AllowedSourcesText);
```

`Normalize()` 用 `TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)` 解析两个时间；任一无效时把两个值都恢复为 `00:00`，合法值统一写为 `HH:mm`。

- [ ] **Step 4: 实现固定规则顺序**

在现有黑名单分支后依次加入来源和时间判断，再保留呼叫、重要人物、重要关键词、@我、普通分支：

```csharp
if (settings.AllowedSources.Count > 0
    && !settings.AllowedSources.Contains(messageEvent.Source, StringComparer.OrdinalIgnoreCase))
{
    return Ignore(messageEvent, "来源不在允许列表");
}

if (settings.RuleTimeWindowEnabled && !IsInsideAllowedWindow(messageEvent.ReceivedAt.TimeOfDay))
{
    return Ignore(messageEvent, "不在允许时间段");
}
```

`IsInsideAllowedWindow` 将 `HH:mm` 转为 `TimeSpan`：`start == end` 返回 true；`start < end` 使用 `time >= start && time < end`；否则使用 `time >= start || time < end`。`Ignore` 只返回固定原因和 `Ignored` 等级，不包含来源值、时间值或消息内容。

- [ ] **Step 5: 写置顶组合失败测试并实现**

```csharp
[Theory]
[InlineData(MessageImportance.Normal, true, false, false, true)]
[InlineData(MessageImportance.Important, false, true, false, true)]
[InlineData(MessageImportance.Calling, false, false, true, true)]
[InlineData(MessageImportance.Calling, false, false, false, false)]
public void TopmostUsesImportanceSetting(
    MessageImportance importance, bool normal, bool important, bool calling, bool expected)
{
    var notifier = new ClassIslandMessageNotifier(new QqListenerSettings
    {
        NormalTopmost = normal,
        ImportantTopmost = important,
        CallingTopmost = calling
    });
    Assert.Equal(expected, notifier.GetTopmost(importance));
}
```

实现：

```csharp
public bool GetTopmost(MessageImportance importance) => importance switch
{
    MessageImportance.Calling => settings.CallingTopmost,
    MessageImportance.Important => settings.ImportantTopmost,
    _ => settings.NormalTopmost
};
```

`CreateRequest` 的 `IsNotificationTopmostEnabled` 改为 `GetTopmost(messageEvent.Importance)`。

- [ ] **Step 6: 增加设置 UI**

在“时间与显示”区为普通、重要、呼叫各加一个 `ToggleSwitch` 绑定对应 Topmost；在“过滤规则”区增加来源多行 `TextBox`、允许时间段开关和两个绑定 `RuleTimeWindowStartText` / `RuleTimeWindowEndText` 的单行 `TextBox`。文案明确来源空值允许全部、时间格式 `HH:mm`、跨午夜自动识别。

- [ ] **Step 7: 验证任务并提交**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter "FullyQualifiedName~MessageRuleEvaluatorTests|FullyQualifiedName~QqListenerSettingsTests|FullyQualifiedName~ClassIslandMessageNotifierTests|FullyQualifiedName~PrivacyTests"`

Expected: PASS，规则边界、优先级、置顶和隐私测试全部通过。

```powershell
git add Models/QqListenerSettings.cs Services/MessageRuleEvaluator.cs Services/ClassIslandMessageNotifier.cs Views/SettingsPages/QqListenerSettingsPage.axaml Tests/QQListener.Tests
git commit -m "feat: 增加来源时间规则和分级置顶"
```

### Task 3: 无副作用规则预览与三个幂等预设

**Files:**
- Create: `Models/MessageRulePreviewResult.cs`
- Create: `Models/QqListenerRulePreset.cs`
- Create: `Services/MessageRulePreviewService.cs`
- Create: `Services/QqListenerRulePresets.cs`
- Create: `Tests/QQListener.Tests/Services/MessageRulePreviewServiceTests.cs`
- Create: `Tests/QQListener.Tests/Services/QqListenerRulePresetsTests.cs`
- Modify: `Plugin.cs`
- Modify: `Views/SettingsPages/QqListenerSettingsPage.cs`
- Modify: `Views/SettingsPages/QqListenerSettingsPage.axaml`
- Modify: `Tests/QQListener.Tests/ServiceRegistrationTests.cs`

- [ ] **Step 1: 写预览无副作用失败测试**

```csharp
[Fact]
public void PreviewUsesParserAndEvaluatorWithoutTouchingRuntimeState()
{
    var settings = new QqListenerSettings { ImportantKeywordsText = "作业" };
    var latest = new LatestMessageState();
    var diagnostics = new QqListenerDiagnosticsState();
    var service = new MessageRulePreviewService(
        new QqToastMessageParser(), new MessageRuleEvaluator(settings),
        new ManualTimeProvider(DateTimeOffset.Parse("2026-07-26T12:00:00+08:00")));

    var result = service.Preview("Alice: 作业", "qq.toast", "12:30");

    Assert.True(result.Success);
    Assert.False(result.IsIgnored);
    Assert.Equal(MessageImportance.Important, result.Importance);
    Assert.Equal("命中重要关键词", result.Reason);
    Assert.False(latest.HasMessage);
    Assert.Equal("暂无", diagnostics.LastAcceptedSummary);
}

[Fact]
public void InvalidPreviewTimeReturnsFailureInsteadOfMutatingSettings()
{
    var settings = new QqListenerSettings { RuleTimeWindowStartText = "08:00" };
    var service = CreateService(settings);
    var result = service.Preview("Alice: hello", "qq.toast", "25:99");
    Assert.False(result.Success);
    Assert.Equal("时间格式必须为 HH:mm", result.Reason);
    Assert.Equal("08:00", settings.RuleTimeWindowStartText);
}

private static MessageRulePreviewService CreateService(QqListenerSettings settings) => new(
    new QqToastMessageParser(),
    new MessageRuleEvaluator(settings),
    new ManualTimeProvider(DateTimeOffset.Parse("2026-07-26T12:00:00+08:00")));
```

- [ ] **Step 2: 运行预览测试并确认失败**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter "FullyQualifiedName~MessageRulePreviewServiceTests"`

Expected: FAIL，预览模型和服务不存在。

- [ ] **Step 3: 实现预览模型和服务**

```csharp
public sealed record MessageRulePreviewResult(
    bool Success,
    MessageImportance Importance,
    bool IsIgnored,
    string Reason);
```

`Preview(string toastText, string source, string? timeText)` 按 `\r\n`/`\n`/`\r` 拆行，空来源使用 `qq.toast`；空时间使用 `TimeProvider` 当前本地时间，非空时间必须严格为 `HH:mm`。Parser 成功后用 `with { Source = source, ReceivedAt = previewTime }` 建临时事件，再直接调用 Evaluator；不引用 Pipeline、LatestMessageState、Diagnostics 或 Hub。

- [ ] **Step 4: 写预设内容、幂等性和边界失败测试**

```csharp
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
```

- [ ] **Step 5: 实现三个稳定预设**

```csharp
public enum QqListenerRulePreset
{
    HomeworkReminder,
    TeacherCalling,
    ExamNotice
}
```

`QqListenerRulePresets.Apply` 使用穷尽 `switch`，每次直接赋相同的 `ImportantPersonsText`、`ImportantKeywordsText`、`CallingEnabled`、`CallingKeyword`；不修改监听、轮询、去重、黑名单、@我、来源、时间和显示字段。未知枚举值抛 `ArgumentOutOfRangeException`。

- [ ] **Step 6: 接入设置页和 DI**

注册 `MessageRulePreviewService`。设置页增加 `PreviewToastText`、`PreviewSourceText = "qq.toast"`、`PreviewTimeText`、`PreviewResultText = "尚未测试"` 四个通知属性；“测试规则”按钮调用 Preview 并显示“重要 / 命中重要关键词”或固定错误。三个预设按钮分别在 `ModifyAndSave` 中调用 `Apply`，按钮上方显示“将替换人物/关键词/呼叫规则，不改来源和时间段”。

- [ ] **Step 7: 验证任务并提交**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter "FullyQualifiedName~MessageRulePreviewServiceTests|FullyQualifiedName~QqListenerRulePresetsTests|FullyQualifiedName~ServiceRegistrationTests"`

Expected: PASS，预览、预设和 DI 测试全部通过。

```powershell
git add Models/MessageRulePreviewResult.cs Models/QqListenerRulePreset.cs Services/MessageRulePreviewService.cs Services/QqListenerRulePresets.cs Plugin.cs Views/SettingsPages Tests/QQListener.Tests
git commit -m "feat: 增加规则测试和官方预设"
```

### Task 4: 自动化事件 Hub 与三个消息触发器

**Files:**
- Create: `Services/MessageAutomationEventHub.cs`
- Create: `Automations/Triggers/MessageReceivedTriggerBase.cs`
- Create: `Automations/Triggers/NormalMessageReceivedTrigger.cs`
- Create: `Automations/Triggers/ImportantMessageReceivedTrigger.cs`
- Create: `Automations/Triggers/CallingMessageReceivedTrigger.cs`
- Create: `Tests/QQListener.Tests/Automations/MessageAutomationEventHubTests.cs`
- Create: `Tests/QQListener.Tests/Automations/MessageReceivedTriggerTests.cs`
- Modify: `Services/QqListenerWorker.cs`
- Modify: `Plugin.cs`
- Modify: `Tests/QQListener.Tests/Services/QqListenerWorkerTests.cs`
- Modify: `Tests/QQListener.Tests/ServiceRegistrationTests.cs`

- [ ] **Step 1: 写 Hub 隔离和 Worker 发布失败测试**

```csharp
[Fact]
public void PublishContinuesAfterSubscriberFailure()
{
    var hub = new MessageAutomationEventHub(NullLogger<MessageAutomationEventHub>.Instance);
    var received = 0;
    hub.MessageReceived += (_, _) => throw new InvalidOperationException("boom");
    hub.MessageReceived += (_, _) => received++;
    hub.Publish(Event(MessageImportance.Important));
    Assert.Equal(1, received);
}

[Fact]
public async Task AcceptedPollEventPublishesOnceBeforeDispatch()
{
    var order = new List<string>();
    var hub = CreateHub(importance => order.Add("hub:" + importance));
    var worker = CreateWorker(new SequencePoller(Accepted(Event("one"))), hub: hub);
    await worker.PollOnceAndDispatchAsync(false, (message, _) =>
    {
        order.Add("dispatch:" + message.Body);
        return Task.CompletedTask;
    }, default);
    Assert.Equal(["hub:Normal", "dispatch:one"], order);
}
```

- [ ] **Step 2: 运行 Hub/Worker 测试并确认失败**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter "FullyQualifiedName~MessageAutomationEventHubTests|FullyQualifiedName~QqListenerWorkerTests"`

Expected: FAIL，Hub 类型和 Worker 构造参数不存在。

- [ ] **Step 3: 实现异常隔离 Hub 并接入 Worker**

Hub 只把等级暴露给 Trigger，不在事件参数中保存正文或发送者；`Publish` 对 `GetInvocationList()` 逐个调用并单独捕获异常，只记录订阅者类型和异常类型。完整实现接口为：

```csharp
public sealed class MessageAutomationEventArgs(MessageImportance importance) : EventArgs
{
    public MessageImportance Importance { get; } = importance;
}

public sealed class MessageAutomationEventHub(ILogger<MessageAutomationEventHub> logger)
{
    internal event EventHandler<MessageAutomationEventArgs>? MessageReceived;

    public void Publish(MessageEvent messageEvent)
    {
        if (messageEvent.Importance == MessageImportance.Ignored) return;
        var args = new MessageAutomationEventArgs(messageEvent.Importance);
        foreach (EventHandler<MessageAutomationEventArgs> handler in
                 MessageReceived?.GetInvocationList().Cast<EventHandler<MessageAutomationEventArgs>>() ?? [])
        {
            try
            {
                handler(this, args);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "QQListener 自动化订阅者执行失败。SubscriberType={SubscriberType}",
                    handler.Target?.GetType().FullName ?? "static");
            }
        }
    }
}
```

Worker 在锁内完成 `_latestMessage.Update(message)` 后、`dispatchAsync` 前调用 `_automationEventHub.Publish(message)`；ReplayLatestAsync 不调用 Hub。测试辅助方法必须把 Hub 传入更新后的 Worker 构造函数，并用 `hub.MessageReceived += (_, args) => callback(message)` 或直接记录 `args.Importance`，不得在 EventArgs 中新增私人字段。

- [ ] **Step 4: 写 Trigger 加载、卸载和等级匹配失败测试**

测试基类提供的可覆盖 `RaiseTrigger()`：重复 `Loaded()` 不重复订阅；`UnLoaded()` 可重复；Normal/Important/Calling 只匹配各自等级；Ignored 不匹配。

```csharp
[Theory]
[InlineData(MessageImportance.Normal, MessageImportance.Normal, 1)]
[InlineData(MessageImportance.Normal, MessageImportance.Important, 0)]
[InlineData(MessageImportance.Important, MessageImportance.Important, 1)]
[InlineData(MessageImportance.Calling, MessageImportance.Calling, 1)]
[InlineData(MessageImportance.Calling, MessageImportance.Ignored, 0)]
public void LoadedTriggerMatchesExactlyOnce(
    MessageImportance target, MessageImportance published, int expected)
{
    var hub = CreateHub();
    var trigger = new RecordingTrigger(hub, target);
    trigger.Loaded();
    trigger.Loaded();
    hub.Publish(Event(published));
    Assert.Equal(expected, trigger.Count);
    trigger.UnLoaded();
    hub.Publish(Event(target));
    Assert.Equal(expected, trigger.Count);
}

private sealed class RecordingTrigger(
    MessageAutomationEventHub hub,
    MessageImportance target) : MessageReceivedTriggerBase(hub, target)
{
    public int Count { get; private set; }
    protected override void RaiseTrigger() => Count++;
}
```

- [ ] **Step 5: 实现并注册三个 ClassIsland Trigger**

`MessageReceivedTriggerBase : TriggerBase` 保存目标等级和 `_loaded`；Loaded/UnLoaded 幂等订阅 Hub；处理器精确比较 Importance 后调用 `protected virtual RaiseTrigger() => Trigger()`。三个具体类只提供以下稳定元数据：

```csharp
[TriggerInfo("QQListener.NormalMessageReceived", "收到普通 QQ 消息时", "\uE02B")]
public sealed class NormalMessageReceivedTrigger(MessageAutomationEventHub hub)
    : MessageReceivedTriggerBase(hub, MessageImportance.Normal);

[TriggerInfo("QQListener.ImportantMessageReceived", "收到重要 QQ 消息时", "\uE7C1")]
public sealed class ImportantMessageReceivedTrigger(MessageAutomationEventHub hub)
    : MessageReceivedTriggerBase(hub, MessageImportance.Important);

[TriggerInfo("QQListener.CallingMessageReceived", "收到 QQ 呼叫时", "\uE02A")]
public sealed class CallingMessageReceivedTrigger(MessageAutomationEventHub hub)
    : MessageReceivedTriggerBase(hub, MessageImportance.Calling);
```

在 Plugin 中使用 `services.AddTrigger<T>()` 注册三项，不创建设置控件。

- [ ] **Step 6: 验证任务并提交**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter "FullyQualifiedName~MessageAutomationEventHubTests|FullyQualifiedName~MessageReceivedTriggerTests|FullyQualifiedName~QqListenerWorkerTests|FullyQualifiedName~ServiceRegistrationTests"`

Expected: PASS，Hub、Trigger、Worker 和 DI 测试全部通过。

```powershell
git add Services/MessageAutomationEventHub.cs Automations/Triggers Services/QqListenerWorker.cs Plugin.cs Tests/QQListener.Tests
git commit -m "feat: 增加消息自动化触发器"
```

### Task 5: 清空行动与最新消息组件过滤/隐私

**Files:**
- Create: `Automations/Actions/ClearLatestMessageAction.cs`
- Create: `Components/LatestQqMessagePresenter.cs`
- Create: `Tests/QQListener.Tests/Automations/ClearLatestMessageActionTests.cs`
- Create: `Tests/QQListener.Tests/Components/LatestQqMessagePresenterTests.cs`
- Modify: `Plugin.cs`
- Modify: `Models/ComponentSettings/LatestQqMessageComponentSettings.cs`
- Modify: `Components/LatestQqMessageComponent.cs`
- Modify: `Components/LatestQqMessageComponent.axaml`
- Modify: `Components/LatestQqMessageComponentSettingsControl.axaml`

- [ ] **Step 1: 写第三行动失败测试**

```csharp
[Fact]
public async Task ActionClearsEveryLatestMessageField()
{
    var state = FilledState();
    var action = new ClearLatestMessageAction(state);
    var method = typeof(ClearLatestMessageAction).GetMethod(
        "OnInvoke", BindingFlags.Instance | BindingFlags.NonPublic)!;
    await (Task)method.Invoke(action, null)!;
    Assert.False(state.HasMessage);
    Assert.Equal("", state.Sender);
    Assert.Equal("", state.Message);
}

[Fact]
public void ActionIdIsStable()
{
    var info = Assert.Single(typeof(ClearLatestMessageAction)
        .GetCustomAttributes(typeof(ActionInfo), false).Cast<ActionInfo>());
    Assert.Equal("QQListener.ClearLatestMessage", info.Id);
}
```

- [ ] **Step 2: 实现并注册清空行动**

```csharp
[ActionInfo("QQListener.ClearLatestMessage", "清空最后一条 QQ 消息", "\uE74D", addDefaultToMenu: false)]
public class ClearLatestMessageAction(LatestMessageState latestMessage) : ActionBase
{
    protected override async Task OnInvoke()
    {
        await base.OnInvoke();
        latestMessage.Clear();
    }
}
```

在 `RegisterBaseActions` 调用 `services.AddAction<ClearLatestMessageAction>()`，并在现有“QQListener 行动”菜单加入同 ID 项，使菜单合计三项。

- [ ] **Step 3: 写 presenter 等级和隐私组合失败测试**

```csharp
[Theory]
[InlineData(MessageImportance.Normal, MessageImportance.Important, false)]
[InlineData(MessageImportance.Important, MessageImportance.Important, true)]
[InlineData(MessageImportance.Calling, MessageImportance.Important, true)]
[InlineData(MessageImportance.Important, MessageImportance.Calling, false)]
public void MinimumImportanceFiltersLatest(
    MessageImportance actual, MessageImportance minimum, bool showsMessage)
{
    var result = Present(actual, new() { MinimumImportance = minimum });
    Assert.Equal(showsMessage, result.HasDisplayedMessage);
}

[Fact]
public void PrivacyModeNeverPlacesSecretsInPresentation()
{
    var result = Present(MessageImportance.Calling, new()
    {
        HideSensitiveContent = true,
        ShowSender = true,
        ShowMessageBody = true
    }, sender: "secret-sender", body: "private-body", conversation: "secret-group");
    var all = string.Join("|", result.StatusText, result.SenderText, result.MessageText);
    Assert.DoesNotContain("secret-sender", all);
    Assert.DoesNotContain("private-body", all);
    Assert.DoesNotContain("secret-group", all);
    Assert.Contains("QQ", all);
    Assert.Contains("呼叫", all);
}

private static LatestQqMessagePresentation Present(
    MessageImportance importance,
    LatestQqMessageComponentSettings settings,
    string sender = "Alice",
    string body = "hello",
    string conversation = "班群")
{
    var state = new LatestMessageState();
    state.Update(new MessageEvent(
        "qq.toast", "QQ", sender, conversation, body,
        DateTimeOffset.Parse("2026-07-26T12:00:00+08:00"), importance,
        [sender, body], $"qq.toast:{importance}"));
    return LatestQqMessagePresenter.Create(state, settings);
}
```

- [ ] **Step 4: 实现纯展示决策**

设置增加默认 `MinimumImportance = Normal`、`ShowMessageBody = true`、`HideSensitiveContent = false`。Presenter 返回 `LatestQqMessagePresentation(IsVisible, HasDisplayedMessage, StatusText, SenderText, MessageText, IsStatusVisible, IsSenderVisible, IsMessageVisible)`：无消息或低于最低等级走现有占位策略；隐私模式令 SenderText 为空，MessageText 只为“{Source} {等级}消息”；正文关闭时 MessageText 为空；不得先构造含敏感文本的隐藏控件值。

- [ ] **Step 5: 让组件只消费 presenter 结果**

`UpdateContent()` 调用 presenter，并把全部 DirectProperty 一次更新；XAML 给正文 TextBlock 增加 `IsVisible="{Binding IsMessageVisible}"`。设置控件增加最低等级下拉框、显示正文和隐私模式开关；最低等级只允许 Normal、Important、Calling。

- [ ] **Step 6: 验证任务并提交**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter "FullyQualifiedName~ClearLatestMessageActionTests|FullyQualifiedName~LatestQqMessagePresenterTests|FullyQualifiedName~LatestMessageStateTests"`

Expected: PASS，行动和 presenter 测试全部通过。

```powershell
git add Automations/Actions/ClearLatestMessageAction.cs Components Models/ComponentSettings Plugin.cs Tests/QQListener.Tests
git commit -m "feat: 增加清空行动和组件隐私过滤"
```

### Task 6: 1.1.0.0 版本、快速开始与变更日志

**Files:**
- Create: `CHANGELOG.md`
- Create: `Tests/QQListener.Tests/Release/DocumentationTests.cs`
- Modify: `Directory.Build.props`
- Modify: `manifest.yml`
- Modify: `QQListener.csproj`
- Modify: `README.md`

- [ ] **Step 1: 写版本和文档失败测试**

```csharp
[Fact]
public void UserDocumentationCoversSupportedScopeAndRecovery()
{
    var root = FindProjectRoot();
    var readme = File.ReadAllText(Path.Combine(root, "README.md"));
    var changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));
    Assert.Contains("五步快速开始", readme);
    Assert.Contains("NT QQ", readme);
    Assert.Contains("TIM", readme);
    Assert.Contains("故障排查", readme);
    Assert.Contains("未知应用身份", readme);
    Assert.Contains("QQListener.NormalMessageReceived", readme);
    Assert.Contains("QQListener.ClearLatestMessage", readme);
    Assert.Contains("1.1.0.0", changelog);
}

private static string FindProjectRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
         directory is not null;
         directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "QQListener.sln")))
            return directory.FullName;
    }
    throw new DirectoryNotFoundException("Unable to find QQListener.sln.");
}
```

扩展 ReleaseMetadataVerifierTests，断言项目版本和 manifest 均为 `1.1.0.0`，构建输出包含 `CHANGELOG.md`。

- [ ] **Step 2: 运行文档测试并确认失败**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter "FullyQualifiedName~DocumentationTests|FullyQualifiedName~ReleaseMetadataVerifierTests"`

Expected: FAIL，CHANGELOG 不存在或版本仍为 1.0.3.0。

- [ ] **Step 3: 同步版本并打包 CHANGELOG**

将 `Directory.Build.props` 唯一 `QQListenerVersion` 和 `manifest.yml` 唯一 `version` 同步改为 `1.1.0.0`；在 csproj 的输出 ItemGroup 增加：

```xml
<None Update="CHANGELOG.md">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

- [ ] **Step 4: 编写面向用户的简洁文档**

README 必须包含五步快速开始、通知权限恢复、没有消息/重复消息/未知格式/身份不可用排查、默认支持 NT QQ、TIM 只在格式兼容时可能解析且不承诺应用身份兼容、未知或空身份始终拒绝、预览与诊断不联网、三个 Trigger/Action/组件/预设用法。CHANGELOG 用 Keep a Changelog 风格记录 1.1.0.0 的“新增/改进/兼容性”，不复制开发过程。

- [ ] **Step 5: 验证任务并提交**

Run:

```powershell
dotnet build QQListener.csproj --configuration Release
dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~DocumentationTests|FullyQualifiedName~ReleaseMetadataVerifierTests"
```

Expected: Build succeeded，所选测试 PASS，`bin/Release/net8.0-windows10.0.19041.0/CHANGELOG.md` 存在。

```powershell
git add CHANGELOG.md README.md Directory.Build.props manifest.yml QQListener.csproj Tests/QQListener.Tests/Release
git commit -m "docs: 发布 QQListener 1.1.0.0 说明"
```

### Task 7: 安全干净安装校验与 CI 门禁

**Files:**
- Modify: `Tools/QQListener.ReleaseVerifier/ReleaseMetadataVerifier.cs`
- Modify: `Tests/QQListener.Tests/Release/ReleaseMetadataVerifierTests.cs`
- Modify: `.github/workflows/build.yml`
- Modify: `Tests/QQListener.Tests/Release/WorkflowTests.cs`

- [ ] **Step 1: 写必需文件、安全路径和清理失败测试**

Release fixture 默认写入根级 `manifest.yml`、`QQListener.dll`、`README.md`、`CHANGELOG.md`、`icon.png`。增加逐项缺失测试；增加 `../escape.dll`、`nested/../../escape.dll`、大小写重复路径；用 `TemporaryDirectoryFactory` 指向测试目录，分别断言成功和失败后目录不存在。

```csharp
[Theory]
[InlineData("manifest.yml")]
[InlineData("QQListener.dll")]
[InlineData("README.md")]
[InlineData("CHANGELOG.md")]
[InlineData("icon.png")]
public void PackageRequiresCleanInstallRootFiles(string missing)
{
    using var fixture = ReleaseFixture.Valid(version: "1.1.0.0");
    fixture.RebuildArchive(excludedEntry: missing);
    var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");
    Assert.False(result.Success);
    Assert.Contains(result.Errors, x => x.Contains(missing, StringComparison.Ordinal));
}
```

- [ ] **Step 2: 运行发布测试并确认失败**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter "FullyQualifiedName~ReleaseMetadataVerifierTests"`

Expected: FAIL，当前 verifier 不要求 README/CHANGELOG/icon，也未完整解包。

- [ ] **Step 3: 实现先审计后安全解包**

`ReadPackage` 先遍历所有项并验证：正斜杠相对路径、无空段/`.`/`..`/盘符/NUL/反斜杠；大小写不敏感唯一；必需文件必须以精确大小写位于根目录；拒绝嵌套 CIPX、checksums 和 md5。只有整个目录表审计完成且没有新增错误后才创建临时目录；逐项用 `Path.GetFullPath(Path.Combine(temp, entry.FullName))` 并验证以 `temp + DirectorySeparatorChar` 开头，再创建父目录和 `CreateNew` 复制。最后从解包目录读取 manifest/DLL/README/CHANGELOG/icon，比较 manifest、项目、AssemblyVersion 和 FileVersion，finally 递归清理。

- [ ] **Step 4: 固化 CI 顺序**

把现有包验证步骤命名改为 `Verify clean install package` / `Verify tagged clean install package`，命令不变；WorkflowTests 按顺序断言 metadata -> CIPX -> clean install verifier -> upload，并继续断言 locked restore、Release build、xUnit 和 23 样例在其之前。

- [ ] **Step 5: 验证任务并提交**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter "FullyQualifiedName~ReleaseMetadataVerifierTests|FullyQualifiedName~WorkflowTests"`

Expected: PASS，恶意包、缺文件、版本错误和临时目录清理测试全部通过。

```powershell
git add Tools/QQListener.ReleaseVerifier/ReleaseMetadataVerifier.cs Tests/QQListener.Tests/Release .github/workflows/build.yml
git commit -m "build: 增加干净安装发布校验"
```

### Task 8: 全量 Release、CIPX 与 ClassIsland 宿主冒烟

**Files:**
- Create: `docs/release/1.1.0.0-verification.md`
- Generated (ignored): `cipx/QQListener.cipx`
- Generated (ignored): `cipx/checksums.md`

- [ ] **Step 1: 从无残留产物状态执行锁定还原和 Release 构建**

用 PowerShell 只删除仓库内已解析的 `bin`、`obj`、各子项目 `bin/obj` 和 `cipx` 生成目录，先逐个 `Resolve-Path`/父目录校验仍位于 worktree，再删除。随后运行：

```powershell
dotnet restore QQListener.sln --locked-mode
dotnet build QQListener.sln --configuration Release --no-restore -p:ContinuousIntegrationBuild=true
```

Expected: restore/build exit 0，无 warning-as-error 或锁文件漂移。

- [ ] **Step 2: 运行全部自动化门禁**

```powershell
dotnet test QQListener.sln --configuration Release --no-build --no-restore
dotnet run --project Samples/QQListener.SampleVerifier/QQListener.SampleVerifier.csproj --configuration Release --no-build --no-restore
dotnet run --project Tools/QQListener.ReleaseVerifier/QQListener.ReleaseVerifier.csproj --configuration Release --no-build --no-restore -- --root . --configuration Release --metadata-only
```

Expected: xUnit 全部 PASS；样例输出 `23/23`；verifier 输出 `Release metadata verified for 1.1.0.0.`。

- [ ] **Step 3: 重建并审计 CIPX**

```powershell
dotnet build QQListener.csproj --configuration Release --no-restore -p:CreateCipx=true -p:ContinuousIntegrationBuild=true
dotnet run --project Tools/QQListener.ReleaseVerifier/QQListener.ReleaseVerifier.csproj --configuration Release --no-build --no-restore -- --root . --configuration Release
tar -tf cipx/QQListener.cipx
Get-FileHash cipx/QQListener.cipx -Algorithm MD5
```

Expected: verifier 成功；ZIP 根目录包含 manifest、DLL、README、CHANGELOG、icon；实际 MD5 与 `cipx/checksums.md` marker 一致；包内没有 CIPX/checksums/md5sum。

- [ ] **Step 4: 执行 Debug 同步和宿主冒烟**

```powershell
dotnet build QQListener.csproj --configuration Debug --no-restore
Get-ChildItem '..\ClassIsland\ClassIsland.Desktop\bin\Debug\net8.0-windows10.0.19041.0\Plugins\QQListener' | Select-Object Name,Length,LastWriteTime
```

确认同步目录含 `QQListener.dll`、manifest、README、CHANGELOG、icon 且版本 1.1.0.0。若 ClassIsland 可启动，使用其 Debug 可执行文件加 `--quiet -dm` 启动到插件加载完成，读取新产生的日志确认 QQListener 无加载异常后正常关闭；若宿主需要交互，记录实际启动结果、观察项和未自动化原因，不写推测性通过结论。

- [ ] **Step 5: 写入真实验证记录并做最终差异审计**

`docs/release/1.1.0.0-verification.md` 记录日期、commit、SDK、实际测试总数、23/23、CIPX MD5、五个必需文件、同步路径、宿主启动/日志结果和限制。运行：

```powershell
git diff --check
git status --short
git diff --stat master...HEAD
git log --oneline master..HEAD
```

Expected: `git diff --check` 无输出；只有计划内源文件、测试、文档和工作流变更；忽略生成物不进入提交。

- [ ] **Step 6: 提交验证记录**

```powershell
git add docs/release/1.1.0.0-verification.md
git commit -m "test: 记录 1.1.0.0 发布验证"
```

- [ ] **Step 7: 最终完整复验**

Run:

```powershell
dotnet test QQListener.sln --configuration Release --no-build --no-restore
dotnet run --project Samples/QQListener.SampleVerifier/QQListener.SampleVerifier.csproj --configuration Release --no-build --no-restore
dotnet run --project Tools/QQListener.ReleaseVerifier/QQListener.ReleaseVerifier.csproj --configuration Release --no-build --no-restore -- --root . --configuration Release
```

Expected: 全部命令 exit 0，测试全绿、样例 23/23、版本和干净安装包校验为 1.1.0.0。

## 规格覆盖自审表

- O1 未知格式可见诊断：Task 1。
- O2 来源、跨午夜、固定优先级、三等级置顶：Task 2。
- O2 设置页规则测试和三个幂等预设：Task 3。
- O3 三个 Trigger 且忽略/重播不触发：Task 4。
- O3 第三个 Action、组件最低等级/隐藏正文/隐私模式：Task 5。
- O4 快速开始、NT QQ/TIM、故障排查、CHANGELOG、1.1.0.0：Task 6。
- O4 安全干净安装、必需文件、版本和 CI：Task 7。
- O4 全门禁、CIPX/MD5、Debug 同步和真实宿主记录：Task 8。
