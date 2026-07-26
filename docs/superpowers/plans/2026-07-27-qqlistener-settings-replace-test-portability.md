# QQListener 设置原子替换测试跨平台 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让设置原子替换测试在 Windows 与 Unix 上分别验证真实文件系统语义，并在所有平台确定性覆盖替换失败后的文件保全与临时文件清理。

**Architecture:** 在 `QqListenerSettings` 实例内注入一个 internal 四参数替换委托，公开无参构造函数仍绑定原有 `File.Replace`。测试保留真实目录、序列化、flush、备份和 finally 清理，仅在替换系统调用边界注入 `IOException`；打开读句柄用例按当前操作系统断言真实结果。

**Tech Stack:** .NET 8、C# 12、System.IO、System.Text.Json、xUnit 2.9。

## Global Constraints

- 不改变 `Save`、`ModifyAndSave`、设置 JSON、备份轮转或异常传播的公开行为。
- 实例级注入不得产生跨测试共享状态，不新增公开测试 API 或第三方依赖。
- `File.Move` 首次保存分支保持不变；两个现有 `File.Replace` 分支必须保留原参数和 `ignoreMetadataErrors: true`。
- Windows 必须继续验证未共享删除的打开句柄会阻止替换；Unix 必须验证打开读句柄不阻止原子替换。
- 所有平台必须确定性验证真实替换失败后主文件、备份不变，临时文件被清理。
- 默认 NT QQ、未知或空应用身份拒绝、TIM 仅可能兼容、版本 `1.1.0.0` 均不得改变。
- 仅通过进程级 `EnableWindowsTargeting=true` 在非 Windows 环境交叉构建，不修改目标框架或产品配置。
- 生产改动必须先 RED 后 GREEN；新增失败清理断言必须用临时 mutation 证明能捕获残留文件。
- 提交消息使用中文，不 squash 或重写已有提交。

---

### Task 1: 设置替换边界与真实跨平台测试

**Files:**
- Modify: `Models/QqListenerSettings.cs:8-15,212-245`
- Modify: `Tests/QQListener.Tests/Models/QqListenerSettingsTests.cs:158-184`

**Interfaces:**
- Consumes: `File.Replace(string sourceFileName, string destinationFileName, string? destinationBackupFileName, bool ignoreMetadataErrors)`、现有 `InternalsVisibleTo("QQListener.Tests")`。
- Produces: `public QqListenerSettings()` 保持现有构造方式；`internal QqListenerSettings(Action<string, string, string?, bool> replaceFile)` 仅供测试注入；两个替换分支经实例字段 `_replaceFile` 调用。

- [ ] **Step 1: 把打开读句柄测试改为平台真实语义**

将 `FailedReplacePreservesFilesAndDeletesTemporaryFile` 改名为 `OpenReadHandleUsesPlatformReplaceSemantics`。沿用三次设置内容 `first`、`second`、`third` 和当前 `FileShare.Read` 句柄：

```csharp
if (OperatingSystem.IsWindows())
{
    Assert.ThrowsAny<IOException>(() => settings.Save());
    Assert.Equal(primaryBefore, File.ReadAllText(settings.SettingsFilePath));
    Assert.Equal(backupBefore, File.ReadAllText(settings.BackupFilePath));
}
else
{
    settings.Save();
    Assert.Equal("third", ReadString(settings.SettingsFilePath, "BlacklistText"));
    Assert.Equal("second", ReadString(settings.BackupFilePath, "BlacklistText"));
}

Assert.Empty(Directory.GetFiles(directory.Path, "settings.json.*.tmp"));
```

不得用非目标平台直接 `return` 或无断言跳过。Windows 分支继续验证真实共享冲突；Unix 分支验证路径原子替换成功。

- [ ] **Step 2: 写确定性失败清理测试**

增加 `InjectedReplaceFailurePreservesFilesAndDeletesTemporaryFile`。先用默认实例保存 `first`、`second`，记录主文件和备份全文；再用同一目录的新注入实例保存 `third`：

```csharp
string? temporaryPath = null;
var temporaryExistedAtReplace = false;
var failing = new QqListenerSettings((source, _, _, _) =>
{
    temporaryPath = source;
    temporaryExistedAtReplace = File.Exists(source);
    throw new IOException("injected replace failure");
})
{
    SettingsDirectory = directory.Path,
    IsEnabled = false,
    BlacklistText = "third"
};

var exception = Assert.Throws<IOException>(() => failing.Save());

Assert.Equal("injected replace failure", exception.Message);
Assert.True(temporaryExistedAtReplace);
Assert.NotNull(temporaryPath);
Assert.False(File.Exists(temporaryPath));
Assert.Equal(primaryBefore, File.ReadAllText(failing.SettingsFilePath));
Assert.Equal(backupBefore, File.ReadAllText(failing.BackupFilePath));
Assert.Empty(Directory.GetFiles(directory.Path, "settings.json.*.tmp"));
```

该测试必须断言真实文件状态，不能只断言委托调用次数或 mock 参数。

- [ ] **Step 3: 运行测试确认 RED**

Run:

```bash
env DOTNET_ROOT=/tmp/qqlistener-dotnet-8.0.423 PATH=/tmp/qqlistener-dotnet-8.0.423:$PATH DOTNET_ReadyToRun=0 EnableWindowsTargeting=true dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --configuration Release --filter "FullyQualifiedName~QqListenerSettingsTests" --no-restore -m:1
```

Expected: 编译失败，指出 `QqListenerSettings` 尚无接收四参数 `Action` 的构造函数；失败来自缺少替换注入边界，而非测试拼写或运行环境。

- [ ] **Step 4: 实现最小实例级替换边界**

在 `QqListenerSettings` 的实例字段附近增加：

```csharp
private readonly Action<string, string, string?, bool> _replaceFile;

public QqListenerSettings() : this(File.Replace)
{
}

internal QqListenerSettings(Action<string, string, string?, bool> replaceFile)
{
    ArgumentNullException.ThrowIfNull(replaceFile);
    _replaceFile = replaceFile;
}
```

将 `_preserveBackupOnNextSave` 分支和普通备份分支的 `File.Replace(...)` 分别改为 `_replaceFile(...)`；不得改变参数、顺序、首次 `File.Move` 或 finally 清理。

- [ ] **Step 5: 运行定向测试确认 GREEN**

Run:

```bash
env DOTNET_ROOT=/tmp/qqlistener-dotnet-8.0.423 PATH=/tmp/qqlistener-dotnet-8.0.423:$PATH DOTNET_ReadyToRun=0 EnableWindowsTargeting=true dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --configuration Release --filter "FullyQualifiedName~QqListenerSettingsTests" --no-restore -m:1
```

Expected: `QqListenerSettingsTests` 全部通过；macOS 执行 Unix 成功替换分支，确定性注入测试执行失败清理分支。

- [ ] **Step 6: 用临时 mutation 证明清理断言真实性**

仅在工作树中临时让 `SaveCoreAtomic` 的 finally 不删除 `temporaryPath`，运行：

```bash
env DOTNET_ROOT=/tmp/qqlistener-dotnet-8.0.423 PATH=/tmp/qqlistener-dotnet-8.0.423:$PATH DOTNET_ReadyToRun=0 EnableWindowsTargeting=true dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --configuration Release --filter "FullyQualifiedName~InjectedReplaceFailurePreservesFilesAndDeletesTemporaryFile" --no-restore -m:1
```

Expected: FAIL，原因是记录的临时文件仍存在或 `settings.json.*.tmp` 非空。立即恢复 finally，重跑同一测试并确认 PASS。最终生产 diff 不得包含 mutation。

- [ ] **Step 7: 自审、验证并提交**

Run:

```bash
git diff --check
git diff --name-only
git diff -- Models/QqListenerSettings.cs Tests/QQListener.Tests/Models/QqListenerSettingsTests.cs
```

确认生产改动只有实例替换边界和两个原调用点；测试在每个平台都有非空断言；无静态 hook、目标框架或产品身份规则变化。

```bash
git add Models/QqListenerSettings.cs Tests/QQListener.Tests/Models/QqListenerSettingsTests.cs
git commit -m "test: 修正设置替换跨平台验证"
```

Expected: 提交只包含上述两个文件，中文提交成功。

## Final Gate

- [ ] 任务 reviewer 同时给出 spec compliant 与 task quality approved；Critical/Important 必须修复并 scoped re-review。
- [ ] 主代理 fresh 复跑 `QqListenerSettingsTests`，确认失败 `0`。
- [ ] 回到 QQListener OKR 最终加固主计划，运行未过滤的用户指定 fresh 全量门禁。
