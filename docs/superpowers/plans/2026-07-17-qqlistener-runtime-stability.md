# QQListener Runtime Stability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make QQ notification ingestion fail closed, serialize all polls, stop cleanly, preserve settings after failures, honor configured cooldowns, and remove private message content from diagnostics and logs.

**Architecture:** Keep the existing parser, rule evaluator, pipeline, notifier, component, and action boundaries. Extract a testable notification platform/reader result, strict candidate filter, polling coordinator, and listener worker so the ClassIsland provider only owns hosted-service integration.

**Tech Stack:** .NET 8, C# 12, Windows Notification APIs, Avalonia, ClassIsland.PluginSdk, xUnit 2.9.2

---

## File Map

- Create `Tests/QQListener.Tests/QQListener.Tests.csproj`: xUnit test project.
- Create `Tests/QQListener.Tests/TestSupport/*.cs`: deterministic fakes for notification reads, time, and dispatch.
- Create `Services/WindowsNotificationReadResult.cs`: explicit read status and result contract.
- Create `Services/IWindowsNotificationReader.cs`: reader interface consumed by the poller.
- Create `Services/IWindowsNotificationPlatform.cs`: WinRT boundary used by the reader.
- Create `Services/WindowsNotificationPlatform.cs`: actual WinRT listener, permission, and snapshot implementation.
- Modify `Services/WindowsNotificationReader.cs`: permission latch and structured result orchestration.
- Create `Services/QqNotificationCandidateFilter.cs`: strict NT QQ/TIM identity allowlist.
- Create `Services/IQqNotificationPoller.cs`: poll/reset contract.
- Create `Services/QqNotificationPoller.cs`: serialized reads, snapshot state, parsing, and pipeline calls.
- Create `Services/QqListenerWorker.cs`: loop, backoff, enable generation, latest-state updates, and dispatch gating.
- Modify `Services/QqNotificationProvider.cs`: hosted task ownership and delegation.
- Modify `Services/ClassIslandMessageNotifier.cs`: awaitable, cancellable UI dispatch.
- Modify `Models/QqListenerSettings.cs`: atomic persistence and recovery state.
- Modify `Models/QqListenerDiagnosticsState.cs`: settings/read diagnostics without message previews.
- Modify `Models/LatestMessageState.cs`: clear cached private data.
- Modify `Services/MessageEventPipeline.cs`: injected time and cooldown-based cleanup.
- Modify `Services/MessageRuleEvaluator.cs`: redact configured rule values from reasons.
- Modify settings page and automation action files: flush saves and respect disabled state.
- Modify `Plugin.cs`, `QQListener.csproj`, `QQListener.sln`, samples, and README to wire and document behavior.

### Task 1: Add the xUnit test harness

**Files:**
- Create: `Tests/QQListener.Tests/QQListener.Tests.csproj`
- Create: `Tests/QQListener.Tests/TestSupport/ManualTimeProvider.cs`
- Modify: `QQListener.csproj`
- Modify: `QQListener.sln`

- [ ] **Step 1: Add the test project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="6.0.2">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <ProjectReference Include="..\..\QQListener.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Exclude `Tests/**` from the plugin project**

Add `Tests` removals beside the existing `Samples` removals for `Compile`, `AvaloniaResource`, `AvaloniaXaml`, `Content`, `EmbeddedResource`, and `None`.

- [ ] **Step 3: Add a manual clock used by cooldown tests**

```csharp
namespace QQListener.Tests.TestSupport;

internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = now.ToUniversalTime();
    public override DateTimeOffset GetUtcNow() => UtcNow;
    public void Advance(TimeSpan value) => UtcNow += value;
}
```

- [ ] **Step 4: Add the project to the solution and verify discovery**

Run:

```powershell
dotnet sln QQListener.sln add Tests/QQListener.Tests/QQListener.Tests.csproj --solution-folder Tests
dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --list-tests
```

Expected: exit 0 and no tests listed yet.

- [ ] **Step 5: Commit**

```powershell
git add QQListener.csproj QQListener.sln Tests/QQListener.Tests
git commit -m "test: add QQListener xUnit harness"
```

### Task 2: Reject notifications without a trusted QQ identity

**Files:**
- Create: `Services/QqNotificationCandidateFilter.cs`
- Create: `Tests/QQListener.Tests/Services/QqNotificationCandidateFilterTests.cs`
- Modify: `Services/QqNotificationProvider.cs`
- Modify: `Samples/qq-toast-samples.json`
- Modify: `Samples/QQListener.SampleVerifier/Program.cs`

- [ ] **Step 1: Write failing identity tests**

```csharp
using QQListener.Services;

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

    [Fact]
    public void RejectsAnyShapeWhenAppIdentityIsUnavailable()
    {
        var snapshot = new WindowsToastSnapshot(1, "QQ", "Tencent.QQ", false, ["sender", "body"]);
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
```

- [ ] **Step 2: Run the focused tests and confirm they fail**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter FullyQualifiedName~QqNotificationCandidateFilterTests`

Expected: FAIL because `QqNotificationCandidateFilter` does not exist.

- [ ] **Step 3: Implement the narrow filter**

```csharp
namespace QQListener.Services;

public static class QqNotificationCandidateFilter
{
    private static readonly HashSet<string> TrustedDisplayNames =
        new(StringComparer.OrdinalIgnoreCase) { "QQ", "腾讯QQ", "TIM" };

    private static readonly string[] TrustedAppIdMarkers =
        ["tencentqq", "qqnt", "comtencentqq", "tencenttim", "comtencenttim"];

    public static bool IsCandidate(WindowsToastSnapshot snapshot, bool qqOnly)
    {
        if (!snapshot.IsAppIdentityAvailable) return false;
        if (!qqOnly) return true;
        if (TrustedDisplayNames.Contains(snapshot.AppName.Trim())) return true;

        var normalizedAppId = new string(snapshot.AppId
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return TrustedAppIdMarkers.Any(normalizedAppId.Contains);
    }
}
```

- [ ] **Step 4: Route provider filtering only through the new filter**

Replace `IsQqCandidateNotification`, `IsQqNotification`, `ContainsQqIdentity`, and `HasUnknownAppIdentity` with `QqNotificationCandidateFilter.IsCandidate(snapshot, settings.QqOnly)`. Unknown identity is rejected for both modes; disabling `QqOnly` allows only notifications whose non-QQ identity is available.

- [ ] **Step 5: Remove text-shape candidate expectations from samples**

Delete `CandidateWhenAppInfoUnavailable` from `SampleCase`, remove the corresponding checks and JSON fields, and remove `LooksLikeQqToastShape` plus its private marker helper from `QqToastMessageParser`. Keep parser samples because parser correctness remains independently useful.

- [ ] **Step 6: Run tests and sample verification**

Run:

```powershell
dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter FullyQualifiedName~QqNotificationCandidateFilterTests
dotnet run --project Samples/QQListener.SampleVerifier/QQListener.SampleVerifier.csproj
```

Expected: identity tests pass and `Verified 23 QQ toast samples.`

- [ ] **Step 7: Commit**

```powershell
git add Services/QqNotificationCandidateFilter.cs Services/QqNotificationProvider.cs Tests/QQListener.Tests/Services Samples
git commit -m "fix: reject notifications without trusted QQ identity"
```

### Task 3: Return structured read results and latch permission denial

**Files:**
- Create: `Services/WindowsNotificationReadResult.cs`
- Create: `Services/IWindowsNotificationReader.cs`
- Create: `Services/IWindowsNotificationPlatform.cs`
- Create: `Services/WindowsNotificationPlatform.cs`
- Modify: `Services/WindowsNotificationReader.cs`
- Create: `Tests/QQListener.Tests/Services/WindowsNotificationReaderTests.cs`
- Create: `Tests/QQListener.Tests/TestSupport/FakeWindowsNotificationPlatform.cs`
- Modify: `Plugin.cs`

- [ ] **Step 1: Write failing result and permission tests**

Tests must prove these exact sequences:

```csharp
[Fact]
public async Task PermissionDeniedIsNotRequestedAgainWithoutExplicitRetry()
{
    var platform = new FakeWindowsNotificationPlatform { AccessStatus = PlatformAccessStatus.Denied };
    var reader = CreateReader(platform);

    Assert.Equal(WindowsNotificationReadStatus.PermissionDenied,
        (await reader.ReadToastNotificationsAsync(false, default)).Status);
    Assert.Equal(WindowsNotificationReadStatus.PermissionDenied,
        (await reader.ReadToastNotificationsAsync(false, default)).Status);
    Assert.Equal(1, platform.RequestAccessCount);

    await reader.ReadToastNotificationsAsync(true, default);
    Assert.Equal(2, platform.RequestAccessCount);
}

[Fact]
public async Task ReadFailureIsDifferentFromSuccessfulEmptyRead()
{
    var failed = await CreateReader(FakeWindowsNotificationPlatform.FailingRead())
        .ReadToastNotificationsAsync(false, default);
    var empty = await CreateReader(FakeWindowsNotificationPlatform.Empty())
        .ReadToastNotificationsAsync(false, default);

    Assert.Equal(WindowsNotificationReadStatus.ReadFailed, failed.Status);
    Assert.Equal(WindowsNotificationReadStatus.Success, empty.Status);
    Assert.Empty(empty.Notifications);
}
```

- [ ] **Step 2: Run the tests and confirm missing contracts fail compilation**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter FullyQualifiedName~WindowsNotificationReaderTests`

Expected: FAIL because the structured result and platform abstractions do not exist.

- [ ] **Step 3: Add the contracts**

```csharp
public enum WindowsNotificationReadStatus
{
    Success,
    PermissionDenied,
    ListenerUnavailable,
    ReadFailed
}

public sealed record WindowsNotificationReadResult(
    WindowsNotificationReadStatus Status,
    IReadOnlyList<WindowsToastSnapshot> Notifications,
    string Reason,
    Exception? Error = null)
{
    public bool IsSuccess => Status == WindowsNotificationReadStatus.Success;
}

public interface IWindowsNotificationReader
{
    Task<WindowsNotificationReadResult> ReadToastNotificationsAsync(
        bool retryPermission,
        CancellationToken cancellationToken);
}
```

Add a plugin-owned `PlatformAccessStatus` enum with `Unspecified`, `Allowed`, and `Denied`, plus `IWindowsNotificationPlatform.RequestAccessAsync(CancellationToken)` and `ReadToastNotificationsAsync(CancellationToken)`, so tests do not depend on WinRT enums.

- [ ] **Step 4: Move direct WinRT access into `WindowsNotificationPlatform`**

`RequestAccessAsync` must run through `Dispatcher.UIThread.InvokeAsync` when called off the UI thread. `GetNotificationsAsync` maps each toast to `WindowsToastSnapshot`. Cancellation is checked before and after WinRT operations.

- [ ] **Step 5: Implement the reader latch**

Use a `SemaphoreSlim` around access state. If access was denied and `retryPermission` is false, return `PermissionDenied` without calling the platform. On explicit retry, request again. Map listener construction failures to `ListenerUnavailable` and read exceptions to `ReadFailed`; never encode either as an empty successful result.

- [ ] **Step 6: Register interfaces in `Plugin.Initialize`**

```csharp
services.AddSingleton<IWindowsNotificationPlatform, WindowsNotificationPlatform>();
services.AddSingleton<IWindowsNotificationReader, WindowsNotificationReader>();
```

- [ ] **Step 7: Run focused and full tests**

Run:

```powershell
dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter FullyQualifiedName~WindowsNotificationReaderTests
dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 8: Commit**

```powershell
git add Services/WindowsNotification* Services/IWindowsNotification* Plugin.cs Tests/QQListener.Tests
git commit -m "fix: distinguish notification read failures"
```

### Task 4: Serialize polling and preserve state across failed reads

**Files:**
- Create: `Services/IQqNotificationPoller.cs`
- Create: `Services/QqNotificationPoller.cs`
- Create: `Models/QqNotificationPollResult.cs`
- Create: `Tests/QQListener.Tests/Services/QqNotificationPollerTests.cs`
- Create: `Tests/QQListener.Tests/TestSupport/SequencedNotificationReader.cs`
- Modify: `Services/QqNotificationProvider.cs`
- Modify: `Plugin.cs`

- [ ] **Step 1: Write failing state and concurrency tests**

Cover both invariants:

```csharp
[Fact]
public async Task FailedReadDoesNotForgetVisibleNotifications()
{
    var reader = SequencedNotificationReader.Create(
        Success(Snapshot(1, "Alice: one")),
        Failed(),
        Success(Snapshot(1, "Alice: one")));
    var poller = CreatePoller(reader);

    Assert.Empty((await poller.PollAsync(false, default)).AcceptedEvents);
    Assert.Empty((await poller.PollAsync(false, default)).AcceptedEvents);
    Assert.Empty((await poller.PollAsync(false, default)).AcceptedEvents);
}

[Fact]
public async Task ConcurrentPollsNeverReadAtTheSameTime()
{
    var reader = new BlockingNotificationReader();
    var poller = CreatePoller(reader);
    var first = poller.PollAsync(false, default);
    await reader.FirstReadStarted;
    var second = poller.PollAsync(false, default);

    Assert.Equal(1, reader.ReadCount);
    reader.ReleaseFirstRead();
    await Task.WhenAll(first, second);
    Assert.Equal(1, reader.MaxConcurrentReads);
}
```

- [ ] **Step 2: Run tests and confirm they fail**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter FullyQualifiedName~QqNotificationPollerTests`

Expected: FAIL because `QqNotificationPoller` does not exist.

- [ ] **Step 3: Implement the polling contract**

```csharp
public interface IQqNotificationPoller
{
    Task<QqNotificationPollResult> PollAsync(bool retryPermission, CancellationToken cancellationToken);
    void ResetDetectionState();
}

public sealed record QqNotificationPollResult(
    WindowsNotificationReadStatus ReadStatus,
    IReadOnlyList<MessageEvent> AcceptedEvents,
    string Reason)
{
    public bool IsSuccess => ReadStatus == WindowsNotificationReadStatus.Success;
}
```

- [ ] **Step 4: Move provider snapshot state into the poller**

The poller owns `_knownNotificationKeys`, `_hasInitializedNotifications`, `_pollGate`, and a small `_stateLock`. `PollAsync` acquires `_pollGate` before calling the reader and releases it in `finally`. Only successful read results may initialize, add/remove notification IDs, or call `UpdateActiveEvents`.

- [ ] **Step 5: Keep parsing and pipeline behavior equivalent on successful reads**

Call `QqNotificationCandidateFilter.IsCandidate(snapshot, settings.QqOnly)` for every snapshot, preserve first-success baseline suppression, and return accepted events without updating `LatestMessageState` or invoking ClassIsland.

- [ ] **Step 6: Wire poller registration**

```csharp
services.AddSingleton<IQqNotificationPoller, QqNotificationPoller>();
```

- [ ] **Step 7: Run focused and full tests**

Run:

```powershell
dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter FullyQualifiedName~QqNotificationPollerTests
dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj
```

Expected: all tests pass, including `MaxConcurrentReads == 1`.

- [ ] **Step 8: Commit**

```powershell
git add Services/IQqNotificationPoller.cs Services/QqNotificationPoller.cs Models/QqNotificationPollResult.cs Services/QqNotificationProvider.cs Plugin.cs Tests/QQListener.Tests
git commit -m "refactor: serialize QQ notification polling"
```

### Task 5: Own the listener task and block dispatch after disable or stop

**Files:**
- Create: `Services/QqListenerWorker.cs`
- Create: `Tests/QQListener.Tests/Services/QqListenerWorkerTests.cs`
- Modify: `Services/QqNotificationProvider.cs`
- Modify: `Services/ClassIslandMessageNotifier.cs`
- Modify: `Models/LatestMessageState.cs`
- Modify: `Plugin.cs`

- [ ] **Step 1: Write failing worker tests**

Tests must verify:

```csharp
[Fact]
public async Task DisableDuringPollDropsResultAndClearsLatestMessage()
{
    var poller = new BlockingPoller(Event("private"));
    var settings = new QqListenerSettings { IsEnabled = true };
    var latest = new LatestMessageState();
    latest.Update(Event("old"));
    var dispatched = new List<MessageEvent>();
    var worker = CreateWorker(settings, poller, latest);

    var run = worker.PollOnceAndDispatchAsync(false, (e, _) =>
    {
        dispatched.Add(e);
        return Task.CompletedTask;
    }, default);
    await poller.Started;
    settings.IsEnabled = false;
    poller.Release();
    await run;

    Assert.Empty(dispatched);
    Assert.False(latest.HasMessage);
}

[Fact]
public async Task RunAsyncCompletesWhenCancelledDuringARead()
{
    using var cts = new CancellationTokenSource();
    var worker = CreateWorker(new QqListenerSettings(), new CancellablePoller(), new LatestMessageState());
    var run = worker.RunAsync((_, _) => Task.CompletedTask, cts.Token);
    cts.Cancel();
    await run.WaitAsync(TimeSpan.FromSeconds(2));
}
```

- [ ] **Step 2: Run tests and confirm they fail**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter FullyQualifiedName~QqListenerWorkerTests`

Expected: FAIL because the worker and `LatestMessageState.Clear` do not exist.

- [ ] **Step 3: Implement `LatestMessageState.Clear`**

Clear every private field (`Sender`, `Message`, `Conversation`, `Source`, timestamps and importance flags), set `HasMessage=false`, then raise the same property notifications used by `Update`.

- [ ] **Step 4: Implement the listener worker**

The worker subscribes to `QqListenerSettings.PropertyChanged`, increments an `Interlocked` generation on every `IsEnabled` change, clears latest state on disable, and resets the poller on enable. `PollOnceAndDispatchAsync` captures the generation before polling and only updates/dispatches events when the generation is unchanged, settings remain enabled, and cancellation is not requested.

`RunAsync` loops until cancellation. Successful polls use `Math.Clamp(ScanIntervalSeconds, 0.1, 5)`; consecutive failures use exponential backoff capped at 30 seconds. Cancellation from polling or delay exits normally.

- [ ] **Step 5: Make notification display awaitable**

Add `ClassIslandMessageNotifier.ShowAsync(MessageEvent, Action<NotificationRequest>, CancellationToken)`. Use `Dispatcher.UIThread.InvokeAsync` with the token when off-thread; keep `CreateRequest` pure.

- [ ] **Step 6: Reduce provider to hosted lifecycle ownership**

Store `_listenerTask`. Start it once with the linked token. `StopAsync` cancels and awaits the exact task before disposing the CTS and marking diagnostics stopped. Manual poll delegates to the worker with `retryPermission: true`. `ResetDetectionState` delegates to the poller.

- [ ] **Step 7: Run focused tests and the full suite**

Run:

```powershell
dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter FullyQualifiedName~QqListenerWorkerTests
dotnet test QQListener.sln
```

Expected: all tests pass and no test exceeds two seconds.

- [ ] **Step 8: Commit**

```powershell
git add Services/QqListenerWorker.cs Services/QqNotificationProvider.cs Services/ClassIslandMessageNotifier.cs Models/LatestMessageState.cs Plugin.cs Tests/QQListener.Tests
git commit -m "fix: stop QQ listener without late dispatch"
```

### Task 6: Make settings persistence atomic and recover fail closed

**Files:**
- Modify: `Models/QqListenerSettings.cs`
- Modify: `Models/QqListenerDiagnosticsState.cs`
- Modify: `Plugin.cs`
- Modify: `Views/SettingsPages/QqListenerSettingsPage.cs`
- Create: `Tests/QQListener.Tests/Models/QqListenerSettingsTests.cs`

- [ ] **Step 1: Write failing persistence tests**

Cover these concrete cases:

```csharp
[Fact]
public void CorruptPrimaryLoadsBackupDisabledWithoutOverwritingPrimary()
{
    using var directory = new TemporaryDirectory();
    File.WriteAllText(directory.File("settings.json"), "{broken");
    File.WriteAllText(directory.File("settings.json.bak"), "{\"IsEnabled\":true,\"BlacklistText\":\"secret\"}");

    var settings = QqListenerSettings.Load(directory.Path);

    Assert.False(settings.IsEnabled);
    Assert.Equal("secret", settings.BlacklistText);
    Assert.True(settings.HasLoadWarning);
    Assert.Equal("{broken", File.ReadAllText(directory.File("settings.json")));
}

[Fact]
public void CorruptPrimaryAndBackupUseDisabledDefaultsWithoutOverwritingFiles()
{
    using var directory = new TemporaryDirectory();
    File.WriteAllText(directory.File("settings.json"), "bad-primary");
    File.WriteAllText(directory.File("settings.json.bak"), "bad-backup");

    var settings = QqListenerSettings.Load(directory.Path);

    Assert.False(settings.IsEnabled);
    Assert.True(settings.HasLoadWarning);
    Assert.Equal("bad-primary", File.ReadAllText(directory.File("settings.json")));
}

[Fact]
public void SaveCreatesBackupAndLeavesValidJson()
{
    using var directory = new TemporaryDirectory();
    var settings = new QqListenerSettings { SettingsDirectory = directory.Path, BlacklistText = "first" };
    settings.Save();
    settings.BlacklistText = "second";
    settings.Save();

    Assert.Contains("second", File.ReadAllText(settings.SettingsFilePath));
    Assert.Contains("first", File.ReadAllText(settings.BackupFilePath));
}
```

- [ ] **Step 2: Run tests and confirm current direct-write behavior fails**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter FullyQualifiedName~QqListenerSettingsTests`

Expected: FAIL because recovery metadata/backup behavior does not exist.

- [ ] **Step 3: Implement atomic save**

Expose JSON-ignored `BackupFilePath`, `HasLoadWarning`, and `LoadWarning`. Serialize to a unique temp file in the same directory using `FileStream`; call `Flush(true)`. Use `File.Replace(temp, primary, backup, true)` when the primary exists and `File.Move(temp, primary)` otherwise. Delete only the temp file on failure and rethrow.

- [ ] **Step 4: Implement fail-closed load**

Try primary, then backup. Any recovery path forces `IsEnabled=false`, sets a non-private warning, and returns without writing. If both fail, return disabled defaults. Preserve the corrupt files for diagnosis.

- [ ] **Step 5: Skip startup normalization save after recovery**

In `Plugin.Initialize`, only call `TrySaveSettings` when `HasLoadWarning` is false. Record the warning into diagnostics after constructing `QqListenerDiagnosticsState`.

- [ ] **Step 6: Flush dirty settings on detach**

Track `_isDirty`. Attach and detach the settings event in `OnAttachedToVisualTree`/`OnDetachedFromVisualTree`. Before stopping the timer on detach, call one guarded `SavePendingSettings`; catch exceptions and call `Diagnostics.RecordError`.

- [ ] **Step 7: Run settings and full tests**

Run:

```powershell
dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter FullyQualifiedName~QqListenerSettingsTests
dotnet test QQListener.sln
```

Expected: all tests pass and corrupt fixture contents remain unchanged.

- [ ] **Step 8: Commit**

```powershell
git add Models/QqListenerSettings.cs Models/QqListenerDiagnosticsState.cs Plugin.cs Views/SettingsPages/QqListenerSettingsPage.cs Tests/QQListener.Tests
git commit -m "fix: persist QQListener settings atomically"
```

### Task 7: Honor long cooldowns and redact private data

**Files:**
- Modify: `Services/MessageEventPipeline.cs`
- Modify: `Services/MessageRuleEvaluator.cs`
- Modify: `Services/QqNotificationPoller.cs`
- Modify: `Models/QqListenerDiagnosticsState.cs`
- Modify: `Automations/Actions/ShowLatestMessageAction.cs`
- Modify: `Views/SettingsPages/QqListenerSettingsPage.cs`
- Create: `Tests/QQListener.Tests/Services/MessageEventPipelineTests.cs`
- Create: `Tests/QQListener.Tests/Services/PrivacyTests.cs`

- [ ] **Step 1: Write the failing 3600-second cooldown test**

```csharp
[Fact]
public void CleanupNeverExpiresBeforeConfiguredCooldown()
{
    var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-17T00:00:00Z"));
    var settings = new QqListenerSettings { CooldownSeconds = 3600 };
    var pipeline = CreatePipeline(settings, time);
    Assert.True(pipeline.Process(Event("A")).Accepted);

    time.Advance(TimeSpan.FromMinutes(11));
    Assert.True(pipeline.Process(Event("B")).Accepted);
    Assert.False(pipeline.Process(Event("A")).Accepted);

    time.Advance(TimeSpan.FromMinutes(50));
    Assert.True(pipeline.Process(Event("A")).Accepted);
}
```

- [ ] **Step 2: Write failing privacy assertions**

Verify that evaluating a message containing `private-body` against configured values `secret-blacklist` and `secret-person` does not put any of those strings in `MessageRuleEvaluationResult.Reason`, `LastAcceptedSummary`, `LastIgnoredReason`, or captured Information logs.

- [ ] **Step 3: Run focused tests and confirm failures**

Run: `dotnet test Tests/QQListener.Tests/QQListener.Tests.csproj --filter "FullyQualifiedName~MessageEventPipelineTests|FullyQualifiedName~PrivacyTests"`

Expected: cooldown and privacy tests fail against current behavior.

- [ ] **Step 4: Inject `TimeProvider` into the pipeline**

Use `timeProvider.GetUtcNow()` instead of `DateTimeOffset.Now`. Cleanup records only when `now - seenAt >= TimeSpan.FromSeconds(Math.Clamp(settings.CooldownSeconds, 1, 3600))`.

- [ ] **Step 5: Redact rule and diagnostic text**

Return fixed reasons: `命中黑名单规则`, `命中呼叫关键词`, `命中重要人物规则`, `命中重要关键词`, `检测到有人@我`, and `未命中特殊规则`. `RecordAccepted` stores only time, importance, and reason. Provider/poller Information logs store importance, text length, source, and reason category, never sender/body/rule values.

- [ ] **Step 6: Gate all replay paths**

Inject `QqListenerSettings` into `ShowLatestMessageAction`; return when disabled. Recheck settings in the settings-page handler. Both call the awaitable provider display method.

- [ ] **Step 7: Run full verification**

Run:

```powershell
dotnet test QQListener.sln
dotnet run --project Samples/QQListener.SampleVerifier/QQListener.SampleVerifier.csproj
```

Expected: all xUnit tests pass and the sample verifier reports 23 verified samples.

- [ ] **Step 8: Commit**

```powershell
git add Services Models Automations Views Tests Samples
git commit -m "fix: honor cooldown and redact message diagnostics"
```

### Task 8: Runtime regression audit and documentation

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-07-17-qqlistener-stability-release-design.md` only if implementation exposes a proven design correction

- [ ] **Step 1: Document fail-closed NT QQ behavior**

State that QQListener requires Windows to expose the notification application identity; unknown identity is intentionally ignored and the diagnostics page shows the reason. Document that disabling listening clears the cached latest message.

- [ ] **Step 2: Run the complete runtime gate**

Run:

```powershell
dotnet build QQListener.sln --configuration Release
dotnet test QQListener.sln --configuration Release --no-build
dotnet run --project Samples/QQListener.SampleVerifier/QQListener.SampleVerifier.csproj --configuration Release --no-build
```

Expected: build exit 0, all xUnit tests pass, and 23 samples verify.

- [ ] **Step 3: Audit forbidden behavior with repository searches**

Run:

```powershell
rg -n "LooksLikeQqToastShape|MessagePreview|命中黑名单:|命中重要人物:|命中重要关键词:" Services Models Automations Views
rg -n "Task.Run\(|_listenerTask|SemaphoreSlim" Services/QqNotificationProvider.cs Services/QqNotificationPoller.cs
```

Expected: the first command finds no runtime usage; the second shows a stored listener task and the poll gate.

- [ ] **Step 4: Commit**

```powershell
git add README.md docs/superpowers/specs/2026-07-17-qqlistener-stability-release-design.md
git commit -m "docs: describe fail-closed QQ notification handling"
```
