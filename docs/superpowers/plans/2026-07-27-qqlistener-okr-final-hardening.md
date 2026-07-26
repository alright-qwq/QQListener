# QQListener OKR 最终加固 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复最终复审发现的 CIPX 资源耗尽风险，补齐隐私与自动化回归证据，收紧 1.1.0.0 宿主冒烟结论，并完成可合并、可推送的 fresh 发布验证。

**Architecture:** 保留现有 ZIP 路径安全与冲突算法，在校验器外围增加“包文件 → checksum → ZIP 预扫描 → 有界解压”的顺序门禁；资源预算由一个可独立测试的内部计数器同时约束声明长度和实际复制长度。产品侧只修复隐私占位显示，Replay 与 Hub 日志保持现有语义并用直接行为测试锁定。发布文档明确区分已证明事实、无法唯一归因的加载路径和正常退出限制。

**Tech Stack:** .NET 8、C# 12、xUnit 2.9、System.IO.Compression、System.Security.Cryptography、Microsoft.Extensions.Logging、Markdown。

## Global Constraints

- 默认面向新版 NT QQ。
- 未知或空应用身份始终拒绝，不因关闭 `QqOnly` 而放宽。
- TIM 仅可能兼容，不承诺应用身份兼容。
- 保持版本 `1.1.0.0`、现有 Trigger/Action ID 和中文提交历史。
- 不移动历史标签，不重写或 squash 现有提交。
- CIPX 文件最大 `256 MiB`。
- `checksums.md` 最大 `1 MiB`，通过文件长度门禁后再读取 marker。
- ZIP 条目最多 `512` 个。
- 每个普通条目最大 `128 MiB`。
- 所有条目累计实际解压量最大 `256 MiB`。
- `manifest.yml` 继续使用更严格的 `1 MiB` 上限。
- `QQListener.dll` 继续使用 `128 MiB` 上限。
- checksum 必须先于 ZIP 打开与临时解压目录创建；MD5 必须流式计算。
- ZIP 预扫描和实际复制必须分别计数，错误不得回显条目内容。
- 当前非 Windows 环境只允许通过进程级 `EnableWindowsTargeting=true` 交叉构建，不修改项目目标框架或产品配置。
- 所有新生产行为先 RED 后 GREEN；已有正确行为的新增回归测试必须通过临时 mutation 证明能捕获回归。
- 提交消息使用中文。

---

## File Structure

- `Tools/QQListener.ReleaseVerifier/ReleaseMetadataVerifier.cs`：包文件大小、流式 MD5、ZIP 预扫描和双重解压预算。
- `Tests/QQListener.Tests/Release/ReleaseMetadataVerifierTests.cs`：CIPX/条目/累计预算、checksum 顺序与流式校验回归。
- `Components/LatestQqMessagePresenter.cs`：隐私模式脱敏占位的展示语义。
- `Tests/QQListener.Tests/Components/LatestQqMessagePresenterTests.cs`：隐私模式与关闭正文组合回归。
- `Tests/QQListener.Tests/Services/QqListenerWorkerTests.cs`：Replay 不发布自动化 Hub 的直接回归。
- `Tests/QQListener.Tests/Automations/MessageAutomationEventHubTests.cs`：订阅者异常日志的隐私边界。
- `docs/release/1.1.0.0-verification.md`：作废证据、clean smoke 边界、fresh 门禁数量和最终 CIPX/MD5。

### Task 1: CIPX 有界流式验证

**Files:**
- Modify: `Tools/QQListener.ReleaseVerifier/ReleaseMetadataVerifier.cs:14-362`
- Modify: `Tests/QQListener.Tests/Release/ReleaseMetadataVerifierTests.cs:10-681`

**Interfaces:**
- Consumes: 现有 `VerifyPackage(string rootPath, string configuration)`、`TemporaryDirectoryFactory`、路径安全和文件/目录冲突审计。
- Produces: checksum-first 的 `VerifyPackage`；内部 `ArchiveResourceBudget`，提供 `AuditDeclared(string entryPath, long length, long entryLimit)` 与 `CopyEntry(Stream input, Stream output, string entryPath, long entryLimit)`；不改变公开 CLI 参数和成功输出。

- [ ] **Step 1: 写 CIPX 大小与 checksum 顺序失败测试**

在 `ReleaseMetadataVerifierTests` 增加三个真实包行为测试。超大 CIPX 和超大 checksum 使用稀疏文件避免大内存分配；checksum mismatch 复用现有 fixture 并断言临时目录工厂从未调用：

```csharp
[Fact]
public void OversizedCipxIsRejectedBeforeChecksumOrExtraction()
{
    using var fixture = ReleaseFixture.Create();
    using (var stream = new FileStream(fixture.CipxFile, FileMode.Open, FileAccess.Write, FileShare.None))
    {
        stream.SetLength(256L * 1024 * 1024 + 1);
    }

    AssertScanRejectsBeforeExtraction(fixture, "CIPX file is too large");
}

[Fact]
public void ChecksumMismatchDoesNotCreateTemporaryDirectory()
{
    using var fixture = ReleaseFixture.Create(checksumOverride: new string('0', 32));

    AssertScanRejectsBeforeExtraction(fixture, "checksum");
}

[Fact]
public void OversizedChecksumsFileIsRejectedBeforeExtraction()
{
    using var fixture = ReleaseFixture.Create();
    using (var stream = new FileStream(
               fixture.ChecksumsFile, FileMode.Open, FileAccess.Write, FileShare.None))
    {
        stream.SetLength(1024L * 1024 + 1);
    }

    AssertScanRejectsBeforeExtraction(fixture, "checksums.md is too large");
}
```

给 fixture 增加只读 `public string CipxFile => CipxPath;` 和 `public string ChecksumsFile => File(Path.Combine("cipx", "checksums.md"));`。预期：大小测试因缺少文件门禁而失败；checksum mismatch 因当前先解压再验 checksum 而失败。

- [ ] **Step 2: 运行定向测试确认 RED**

Run:

```bash
env EnableWindowsTargeting=true dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --configuration Release --filter "FullyQualifiedName~OversizedCipxIsRejectedBeforeChecksumOrExtraction|FullyQualifiedName~ChecksumMismatchDoesNotCreateTemporaryDirectory|FullyQualifiedName~OversizedChecksumsFileIsRejectedBeforeExtraction"
```

Expected: 三项均 FAIL；失败分别来自超大文件未被前置拒绝、临时目录工厂被调用。

- [ ] **Step 3: 写条目数和声明预算失败测试**

将现有 `PackageAcceptsFiveThousandIndependentEntries` 改为 512 条目边界测试，并增加 513 条拒绝；五个必需条目已计入总数，所以额外条目分别为 507 和 508：

```csharp
[Fact]
public void PackageAcceptsMaximumEntryCount()
{
    var extras = Enumerable.Range(0, 507)
        .Select(index => new ArchiveItem($"assets/{index:D4}.txt", string.Empty))
        .ToArray();
    using var fixture = ReleaseFixture.Create(extraEntries: extras);

    var result = ReleaseMetadataVerifier.VerifyPackage(fixture.Path, "Release");

    Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
}

[Fact]
public void PackageRejectsEntryCountOverLimitBeforeExtraction()
{
    var extras = Enumerable.Range(0, 508)
        .Select(index => new ArchiveItem($"assets/{index:D4}.txt", string.Empty))
        .ToArray();
    using var fixture = ReleaseFixture.Create(extraEntries: extras);

    AssertScanRejectsBeforeExtraction(fixture, "more than 512 entries");
}
```

为声明/实际预算增加内部预算对象的边界测试，期望值全部用手工字面量推导：

```csharp
[Fact]
public void DefaultArchiveBudgetAcceptsExactEntryAndTotalLimits()
{
    var budget = ReleaseMetadataVerifier.CreateArchiveResourceBudget();

    Assert.Null(budget.AuditDeclared("first.bin", 128L * 1024 * 1024, 128L * 1024 * 1024));
    Assert.Null(budget.AuditDeclared("second.bin", 128L * 1024 * 1024, 128L * 1024 * 1024));
}

[Fact]
public void DefaultArchiveBudgetRejectsEntryAndTotalOverflow()
{
    var entryBudget = ReleaseMetadataVerifier.CreateArchiveResourceBudget();
    Assert.Contains("oversized.bin", entryBudget.AuditDeclared(
        "oversized.bin", 128L * 1024 * 1024 + 1, 128L * 1024 * 1024));

    var totalBudget = ReleaseMetadataVerifier.CreateArchiveResourceBudget();
    Assert.Null(totalBudget.AuditDeclared("first.bin", 128L * 1024 * 1024, 128L * 1024 * 1024));
    Assert.Null(totalBudget.AuditDeclared("second.bin", 128L * 1024 * 1024, 128L * 1024 * 1024));
    Assert.Contains("third.bin", totalBudget.AuditDeclared("third.bin", 1, 128L * 1024 * 1024));
}
```

- [ ] **Step 4: 运行条目与声明预算测试确认 RED**

Run:

```bash
env EnableWindowsTargeting=true dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --configuration Release --filter "FullyQualifiedName~PackageAcceptsMaximumEntryCount|FullyQualifiedName~PackageRejectsEntryCountOverLimitBeforeExtraction|FullyQualifiedName~DefaultArchiveBudget"
```

Expected: 512 条测试可通过；513 条当前错误地通过；预算工厂尚不存在导致编译失败。补齐测试编译骨架后，确保行为测试仍以缺少预算为原因失败，再进入 GREEN。

- [ ] **Step 5: 实现包文件门禁、流式 MD5 和声明预算**

在 `ReleaseMetadataVerifier` 增加精确常量：

```csharp
private const long MaxCipxBytes = 256L * 1024 * 1024;
private const long MaxChecksumsBytes = 1024L * 1024;
private const int MaxArchiveEntries = 512;
private const long MaxEntryBytes = 128L * 1024 * 1024;
private const long MaxExtractedBytes = 256L * 1024 * 1024;
```

将 `VerifyPackage` 顺序改为：CIPX/checksums 存在、CIPX `FileInfo.Length <= 256 MiB`、checksums `FileInfo.Length <= 1 MiB` → `VerifyChecksum` → `ReadPackage`。任一前置阶段失败立即返回本阶段错误，不调用 `TemporaryDirectoryFactory`。checksum 文本只在 1 MiB 门禁通过后读取。MD5 改为：

```csharp
using var stream = new FileStream(cipxPath, FileMode.Open, FileAccess.Read, FileShare.Read);
var actual = Convert.ToHexString(MD5.HashData(stream));
```

禁止保留 `File.ReadAllBytes(cipxPath)`。

增加内部预算实现；`CreateArchiveResourceBudget` 位于 `ReleaseMetadataVerifier` 内，`ArchiveResourceBudget` 是同一 namespace 下的 top-level internal 类型，供测试程序集通过现有 `InternalsVisibleTo` 使用。错误只包含固定类型与条目路径：

```csharp
internal static ArchiveResourceBudget CreateArchiveResourceBudget() =>
    new(MaxExtractedBytes);

internal sealed class ArchiveResourceBudget(long totalLimit)
{
    private long _declaredBytes;
    private long _extractedBytes;

    internal string? AuditDeclared(string entryPath, long length, long entryLimit)
    {
        if (length > entryLimit)
            return $"archive entry '{entryPath}' exceeds the allowed size.";
        if (length > totalLimit - _declaredBytes)
            return $"archive entry '{entryPath}' exceeds the total extraction budget.";
        _declaredBytes += length;
        return null;
    }

    internal void CopyEntry(Stream input, Stream output, string entryPath, long entryLimit)
    {
        var buffer = new byte[81920];
        var entryBytes = 0L;
        int bytesRead;
        while ((bytesRead = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (bytesRead > entryLimit - entryBytes
                || bytesRead > totalLimit - _extractedBytes)
                throw new InvalidDataException(
                    $"archive entry '{entryPath}' exceeds the allowed extraction budget.");
            output.Write(buffer, 0, bytesRead);
            entryBytes += bytesRead;
            _extractedBytes += bytesRead;
        }
    }
}
```

ZIP 预扫描先检查 `archive.Entries.Count > 512`，再对每项调用同一个预算对象的 `AuditDeclared`。`manifest.yml` 使用 `1 MiB`，其余包括 `QQListener.dll` 使用 `128 MiB`。只有路径、冲突、必需文件、条目数、单项和累计声明预算全部通过才创建临时目录；实际复制复用同一默认限制的新实际计数器并调用 `CopyEntry`。

- [ ] **Step 6: 写并验证实际复制双重计数测试**

用小流直接验证真实复制逻辑，不创建大文件：

```csharp
[Fact]
public void CopyEntryEnforcesActualEntryAndTotalBytes()
{
    var entryBudget = new ArchiveResourceBudget(totalLimit: 10);
    Assert.Throws<InvalidDataException>(() => entryBudget.CopyEntry(
        new MemoryStream(new byte[6]), Stream.Null, "entry.bin", entryLimit: 5));

    var totalBudget = new ArchiveResourceBudget(totalLimit: 10);
    totalBudget.CopyEntry(new MemoryStream(new byte[6]), Stream.Null, "first.bin", entryLimit: 10);
    var exception = Assert.Throws<InvalidDataException>(() => totalBudget.CopyEntry(
        new MemoryStream(new byte[5]), Stream.Null, "second.bin", entryLimit: 10));
    Assert.Contains("second.bin", exception.Message, StringComparison.Ordinal);
}
```

Run:

```bash
env EnableWindowsTargeting=true dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --configuration Release --filter "FullyQualifiedName~ReleaseMetadataVerifierTests"
```

Expected: 所有 Release verifier 测试 PASS；现有路径安全、大小写重复、保留名和文件/目录冲突测试保持通过。

- [ ] **Step 7: 自审并提交 Task 1**

Run:

```bash
rg -n "ReadAllBytes\(cipxPath\)|MaxCipxBytes|MaxChecksumsBytes|MaxArchiveEntries|MaxEntryBytes|MaxExtractedBytes" Tools/QQListener.ReleaseVerifier/ReleaseMetadataVerifier.cs
git diff --check
git add Tools/QQListener.ReleaseVerifier/ReleaseMetadataVerifier.cs Tests/QQListener.Tests/Release/ReleaseMetadataVerifierTests.cs
git commit -m "fix: 限制发布包资源消耗"
```

同时把 fixture 的 MD5 生成改为 `using var stream = File.OpenRead(CipxPath); MD5.HashData(stream)`，避免测试夹具在未来的大包用例中制造整包内存分配。

Expected: 不再出现 `ReadAllBytes(cipxPath)`；五项文件/解压预算均连接到生产路径；提交成功。

### Task 2: 隐私模式保留脱敏占位

**Files:**
- Modify: `Components/LatestQqMessagePresenter.cs:24-43`
- Modify: `Tests/QQListener.Tests/Components/LatestQqMessagePresenterTests.cs:39-64`

**Interfaces:**
- Consumes: `LatestQqMessagePresenter.Create` 和现有 `GetSource`/`GetImportanceName`。
- Produces: `HideSensitiveContent=true` 时始终显示“来源 + 等级 + 消息”脱敏占位；`ShowMessageBody=false` 仍只对非隐私模式隐藏真实正文。

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void PrivacyModeKeepsRedactedPlaceholderWhenMessageBodyIsDisabled()
{
    var result = Present(MessageImportance.Important, new()
    {
        HideSensitiveContent = true,
        ShowSender = true,
        ShowMessageBody = false
    }, sender: "secret-sender", body: "private-body", conversation: "secret-group");

    Assert.True(result.HasDisplayedMessage);
    Assert.True(result.IsMessageVisible);
    Assert.Equal("QQ 重要消息", result.MessageText);
    Assert.DoesNotContain("secret-sender", result.MessageText, StringComparison.Ordinal);
    Assert.DoesNotContain("private-body", result.MessageText, StringComparison.Ordinal);
    Assert.DoesNotContain("secret-group", result.MessageText, StringComparison.Ordinal);
}
```

- [ ] **Step 2: 运行测试确认 RED**

Run:

```bash
env EnableWindowsTargeting=true dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --configuration Release --filter "FullyQualifiedName~PrivacyModeKeepsRedactedPlaceholderWhenMessageBodyIsDisabled"
```

Expected: FAIL，当前 `MessageText` 为空且 `IsMessageVisible=false`。

- [ ] **Step 3: 写最小实现并验证 GREEN**

把隐私分支的正文选择改为固定脱敏占位：

```csharp
var privacyMessageText =
    $"{GetSource(latestMessage.Source)} {GetImportanceName(latestMessage.Importance)}消息";
```

不要改变非隐私分支、状态标签、发送者可见性或最低重要度过滤。

Run:

```bash
env EnableWindowsTargeting=true dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --configuration Release --filter "FullyQualifiedName~LatestQqMessagePresenterTests"
git diff --check
```

Expected: presenter 测试全部 PASS。

- [ ] **Step 4: 提交 Task 2**

```bash
git add Components/LatestQqMessagePresenter.cs Tests/QQListener.Tests/Components/LatestQqMessagePresenterTests.cs
git commit -m "fix: 保留隐私消息占位"
```

### Task 3: Replay 与 Hub 日志隐私回归

**Files:**
- Modify: `Tests/QQListener.Tests/Services/QqListenerWorkerTests.cs:133-181`
- Modify: `Tests/QQListener.Tests/Automations/MessageAutomationEventHubTests.cs:11-45`

**Interfaces:**
- Consumes: `QqListenerWorker.ReplayLatestAsync`、`MessageAutomationEventHub.Publish` 和内部 `MessageReceived` 事件。
- Produces: 直接证明 Replay 只调度 UI 重放、不发布 Hub；订阅者失败日志只包含订阅者类型和异常类型，logger 不接收异常对象。

- [ ] **Step 1: 增加 Replay 直接回归测试**

```csharp
[Fact]
public async Task ReplayLatestDoesNotPublishAutomationEvent()
{
    var latest = new LatestMessageState();
    latest.Update(Event("private-replay"));
    var hub = new MessageAutomationEventHub(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<MessageAutomationEventHub>.Instance);
    var published = 0;
    hub.MessageReceived += (_, _) => published++;
    var worker = CreateWorker(
        new QqListenerSettings { IsEnabled = true },
        new SequencePoller(Accepted()),
        latest,
        hub: hub);
    var dispatched = 0;

    await worker.ReplayLatestAsync((_, showIfCurrent, _) =>
    {
        showIfCurrent(() => dispatched++);
        return Task.CompletedTask;
    }, default);

    Assert.Equal(1, dispatched);
    Assert.Equal(0, published);
}
```

先运行该测试确认当前正确行为为 PASS；随后仅为 mutation 验证，在 `ReplayLatestAsync` 的 dispatch 前临时加入 `_automationEventHub.Publish(message);`，重跑并确认测试 FAIL，再立即还原该临时行并确认 PASS。最终生产 diff 不得包含 Replay 发布调用。

- [ ] **Step 2: 增加 Hub 错误日志隐私测试**

在测试文件内增加捕获 logger：

```csharp
private sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];
    public List<Exception?> Exceptions { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
        Exceptions.Add(exception);
    }
}
```

新增测试：

```csharp
[Fact]
public void SubscriberFailureLogContainsTypesButNoPrivatePayloadOrExceptionMessage()
{
    var logger = new RecordingLogger<MessageAutomationEventHub>();
    var hub = new MessageAutomationEventHub(logger);
    hub.MessageReceived += (_, _) =>
        throw new InvalidOperationException("secret-exception-message");

    hub.Publish(new MessageEvent(
        "secret-source", "QQ", "secret-sender", "secret-conversation",
        "private-body", DateTimeOffset.Parse("2026-07-26T00:00:00Z"),
        MessageImportance.Important, ["secret-sender", "private-body"],
        "secret-deduplication-key"));

    var text = Assert.Single(logger.Messages);
    Assert.Contains(typeof(InvalidOperationException).FullName!, text, StringComparison.Ordinal);
    Assert.DoesNotContain("secret-exception-message", text, StringComparison.Ordinal);
    Assert.DoesNotContain("secret-sender", text, StringComparison.Ordinal);
    Assert.DoesNotContain("secret-conversation", text, StringComparison.Ordinal);
    Assert.DoesNotContain("private-body", text, StringComparison.Ordinal);
    Assert.All(logger.Exceptions, exception => Assert.Null(exception));
}
```

先运行确认当前正确行为为 PASS；随后仅为 mutation 验证，临时改为 `logger.LogError(exception, "...")`，确认测试 FAIL，再还原并确认 PASS。最终生产文件 `MessageAutomationEventHub.cs` 应无 diff。

- [ ] **Step 3: 运行自动化相关测试并提交**

Run:

```bash
env EnableWindowsTargeting=true dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --configuration Release --filter "FullyQualifiedName~QqListenerWorkerTests|FullyQualifiedName~MessageAutomationEventHubTests|FullyQualifiedName~MessageReceivedTriggerTests"
git diff --check
git add Tests/QQListener.Tests/Services/QqListenerWorkerTests.cs Tests/QQListener.Tests/Automations/MessageAutomationEventHubTests.cs
git commit -m "test: 补齐自动化隐私回归"
```

Expected: 三组测试全部 PASS；提交只包含测试文件。

### Task 4: 收紧宿主冒烟文档结论

**Files:**
- Modify: `docs/release/1.1.0.0-verification.md:1-101`

**Interfaces:**
- Consumes: `badad21..3e2471b` 的历史记录、已作废首次冒烟、最终 GUID 沙箱日志计数。
- Produces: 不把日志唯一归因到 `host\\data` 副本、不把 Provider 启动表述为实际轮询或真实通知链、不暗示提交了原始证据。

- [ ] **Step 1: 独立核对历史修正文档**

Run:

```bash
git diff --stat badad21..3e2471b
git diff --word-diff=plain badad21..3e2471b -- docs/release/1.1.0.0-verification.md
git show --stat --oneline badad21
git show --stat --oneline 3e2471b
```

逐条核对：首次继承旧 `Settings/Config/Logs` 的证据作废；最终使用全新 `host\\data`；复制完整宿主后根 `host\\Plugins` 是否仍有副本无法由 Markdown 唯一排除。

- [ ] **Step 2: 修改宿主冒烟段落和结论**

在“ClassIsland 隔离宿主冒烟”明确写入以下事实：

```text
复制完整宿主后，沙箱根 Plugins 是否仍包含 QQListener 副本、运行时实际选择了根 Plugins 还是 data\\Plugins，本次记录未保存加载路径或 DLL 哈希，无法独立证明。

“QQListener 监听已启动”只证明 QqNotificationProvider、依赖注入和 HostedService 已启动。由于插件设置缺失，监听开关保持关闭，实际通知轮询未启动；该日志不能证明真实通知链。

本 Markdown 是操作者验证记录，仓库未提交原始日志、完整命令 transcript、实际插件加载路径或被加载 DLL 哈希。
```

把结论限定为：

```text
无既有状态的沙箱宿主成功启动，且某个已同步的 QQListener 1.1.0.0 副本完成 Provider 注册。现有证据不能唯一证明加载的是 host\\data 副本，也不能证明实际通知轮询或真实通知链。正常退出仍无法安全自动化；强制结束仅用于已取证沙箱清理。
```

不得删除现有计数：`App started` 1 条、监听启动 1 条、QQListener 错误 0 条、缺设置时不创建 `settings.json`。

- [ ] **Step 3: 人工证据边界审查并提交**

Run:

```bash
rg -n "证据已作废|全新的.*data|根.*Plugins|加载路径|DLL 哈希|Provider|实际通知轮询|真实通知链|正常退出" docs/release/1.1.0.0-verification.md
git diff --check
git add docs/release/1.1.0.0-verification.md
git commit -m "docs: 收紧宿主冒烟结论"
```

Expected: 每一项边界均有明确文字；不新增不存在的原始证据或 Windows 重跑结果。

### Task 5: Fresh 发布门禁与验证记录更新

**Files:**
- Modify: `docs/release/1.1.0.0-verification.md`

**Interfaces:**
- Consumes: Task 1–4 的最终 HEAD 和用户指定的七条验证命令。
- Produces: fresh 测试总数、23/23 样例、1.1.0.0 verifier、CIPX 根文件和新 MD5 的可复核记录。

- [ ] **Step 1: 准备临时 .NET 8 SDK**

安装 `8.0.423` 到仓库外的临时目录，例如 `/tmp/qqlistener-dotnet-8.0.423`。所有后续命令仅通过本进程环境指定：

```bash
env DOTNET_ROOT=/tmp/qqlistener-dotnet-8.0.423 PATH=/tmp/qqlistener-dotnet-8.0.423:$PATH EnableWindowsTargeting=true dotnet --version
```

Expected: `8.0.423`。不得修改 `global.json`、目标框架、Git 或系统代理配置。

- [ ] **Step 2: 执行 fresh restore/build/test/sample**

依次运行且逐条保存退出码与测试数量：

```bash
dotnet restore QQListener.sln --locked-mode
dotnet build QQListener.sln --configuration Release --no-restore -p:ContinuousIntegrationBuild=true
dotnet test QQListener.sln --configuration Release --no-build --no-restore
dotnet run --project Samples/QQListener.SampleVerifier/QQListener.SampleVerifier.csproj --configuration Release --no-build --no-restore
```

每条命令使用 Step 1 的进程级 `DOTNET_ROOT`、`PATH`、`EnableWindowsTargeting=true` 环境。Expected: 全部 exit 0；xUnit 失败 0；样例输出 `Verified 23 QQ toast samples.`。

- [ ] **Step 3: 重建 CIPX 并运行完整 verifier**

```bash
dotnet build QQListener.csproj --configuration Release --no-restore -p:CreateCipx=true -p:ContinuousIntegrationBuild=true
dotnet run --project Tools/QQListener.ReleaseVerifier/QQListener.ReleaseVerifier.csproj --configuration Release --no-build --no-restore -- --root . --configuration Release
git diff --check
```

Expected: build/verifier exit 0；输出 `Release metadata verified for 1.1.0.0.`；`git diff --check` 无输出。

- [ ] **Step 4: 独立核对 CIPX 根文件和 MD5**

使用 ZIP 列表确认五个必需根文件各精确出现一次、无嵌套 CIPX/checksums/md5 生成物；流式计算 `cipx/QQListener.cipx` MD5，并与 `cipx/checksums.md` 唯一 marker 比对。记录包条目数、实际 MD5 和 marker，任何不一致都停止合并。

- [ ] **Step 5: 用 fresh 结果更新验证记录并提交**

把文档基线提交、SDK、xUnit 总数、CIPX 条目列表、MD5 与 verifier 输出更新为本轮实际结果；历史 Windows clean smoke 只更新证据边界，不伪造重跑。

Run:

```bash
git diff --check
git add docs/release/1.1.0.0-verification.md cipx/QQListener.cipx cipx/checksums.md
git commit -m "test: 更新 1.1.0.0 发布验证"
```

如果 `cipx/` 按现有规则未被 Git 跟踪，则只提交文档，不强行改变忽略规则。

---

## Final Review and Integration Gate

- [ ] 生成从分支基线到最终 HEAD 的完整 review package，由独立 reviewer 重点检查安全解包、路径冲突、隐私、Trigger/Replay 行为和测试真实性。
- [ ] 所有 Critical/Important 必须修复并完成 scoped re-review；Minor 逐项裁决并记录。
- [ ] 在最终待合并 HEAD 上重新执行 Task 5 的完整七条门禁，禁止引用修改前结果。
- [ ] 非 squash 合并 `codex/okr-completion` 到 `master`，保留中文提交历史，并确认 `master` 指向已验证提交。
- [ ] 仅对单次 `git push origin master` 清空 `HTTP_PROXY`、`HTTPS_PROXY`、`ALL_PROXY` 及其小写变量；不得永久修改 Git/系统代理。
- [ ] 使用同样的单次无代理环境执行 `git ls-remote origin refs/heads/master`，确认远端哈希与本地 `master` 完全一致。
- [ ] 远端 `master` 推送成功前保留功能分支。
