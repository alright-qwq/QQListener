# 设置页丰富化与关于区实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 QQListener 设置页升级为"页头版本号 + 快捷操作 + 配置 + 诊断 + 关于"五段式，新增 3 个排障按钮和 1 个关于卡片。

**Architecture:** 新建 `QqListenerPluginInfo` 单例承载 manifest 派生的展示元数据（版本/作者/URL/SDK 等）；`QqNotificationProvider` 暴露两个公开方法（手动轮询、重置去重）供设置页按钮调用；`QqListenerSettingsPage` 增加 code-behind 处理按钮点击，axaml 新增三个区段。所有外链通过 ClassIsland 的 `UriNavigationCommands.UriNavigationCommand` 走宿主统一处理。

**Tech Stack:** .NET 8、Avalonia 11、FluentAvalonia.UI.Controls、CommunityToolkit.Mvvm、ClassIsland.PluginSdk 1.7.106.2-dev-v2

---

## File Structure

新建文件：

- `Models/QqListenerPluginInfo.cs` — 单例 POCO，承载 `Version` / `Author` / `Url` / `IssuesUrl` / `QaUrl` / `SdkVersion` / `License` / `HeaderLine` / `CopyrightLine` / `LicenseLine` / `SdkLine`。由 `Plugin.Initialize` 从 `Info.Manifest` 派生构造一次注入 DI。

修改文件：

- `Plugin.cs` — 构造 `QqListenerPluginInfo` 并 `AddSingleton`。
- `Services/QqNotificationProvider.cs` —
  - `private void ResetDetectionState()` → `public`
  - 新增 `public Task PollOnceManuallyAsync()`：直接 `await PollOnceAsync()`，靠 `_pollLock` 串行化；监听未启用时直接返回。
  - 新增内部需要：当前 `PollOnceAsync` 是 `private async Task`，已经够用。
- `Views/SettingsPages/QqListenerSettingsPage.axaml` — 加页头版本行、新增"快捷操作"区、合并诊断区前 4 行为可展开项、新增"关于"区。
- `Views/SettingsPages/QqListenerSettingsPage.cs` — 注入 `QqListenerPluginInfo`，暴露 `PluginInfo` / `ProviderAvailable` / `CanPoll` / `CanReplay` 属性，新增 3 个点击 handler。

不动的文件（确认）：`Models/MessageEvent.cs`、`Services/MessageEventPipeline.cs`、`Services/MessageRuleEvaluator.cs`、`Services/QqToastMessageParser.cs`、`Services/WindowsNotificationReader.cs`、`Models/QqListenerDiagnosticsState.cs`、`Components/**`、`Automations/**`。

---

## Task 1: 新建 QqListenerPluginInfo 单例

**Files:**
- Create: `Models/QqListenerPluginInfo.cs`

- [ ] **Step 1: 写出 QqListenerPluginInfo 类**

创建 `Models/QqListenerPluginInfo.cs`：

```csharp
namespace QQListener.Models;

/// <summary>
/// 把 manifest.yml 派生的展示元数据集中到一处，供设置页"页头版本号"和"关于"区使用。
/// 唯一权威是 manifest.yml；SDK 版本是项目里的 csproj 同步硬编码。
/// </summary>
public sealed class QqListenerPluginInfo
{
    /// <summary>
    /// 与 QQListener.csproj 的 <ClassIslandPluginSdkVersion> 同步维护。
    /// SDK 当前未公开运行时元数据 API，所以暂时双写。
    /// </summary>
    public const string DefaultSdkVersion = "1.7.106.2-dev-v2";

    public const string DefaultLicense = "MIT";
    public const string DefaultCopyrightYear = "2026";
    public const string DefaultQaUrl = "https://al-right.top/archives/58";
    public const string OriginalAuthorLine = "原作者：B 站 @BSOD-MEMZ（PySide6 版）";

    public string Version { get; init; } = "";
    public string Author { get; init; } = "";
    public string Url { get; init; } = "";
    public string IssuesUrl { get; init; } = "";
    public string QaUrl { get; init; } = DefaultQaUrl;
    public string SdkVersion { get; init; } = DefaultSdkVersion;
    public string License { get; init; } = DefaultLicense;

    /// <summary>页头小字："v1.0.1.0 · by alright-qwq"。任一字段缺失时给"未知"占位。</summary>
    public string HeaderLine
    {
        get
        {
            var v = string.IsNullOrWhiteSpace(Version) ? "未知" : Version;
            var a = string.IsNullOrWhiteSpace(Author) ? "未知作者" : Author;
            return $"v{v} · by {a}";
        }
    }

    /// <summary>关于卡片："Copyright © 2026 alright-qwq"。</summary>
    public string CopyrightLine =>
        $"Copyright © {DefaultCopyrightYear} {(string.IsNullOrWhiteSpace(Author) ? "未知作者" : Author)}";

    /// <summary>关于卡片："Released under the MIT License"。</summary>
    public string LicenseLine => $"Released under the {License} License";

    /// <summary>关于卡片："基于 ClassIsland.PluginSdk X.Y.Z"。</summary>
    public string SdkLine => $"基于 ClassIsland.PluginSdk {SdkVersion}";

    /// <summary>关于卡片底部固定行。</summary>
    public string OriginalAuthor => OriginalAuthorLine;

    /// <summary>设计器与未通过宿主加载场景的占位实例。</summary>
    public static QqListenerPluginInfo Designer { get; } = new()
    {
        Version = "设计器",
        Author = "alright-qwq",
        Url = "https://github.com/alright-qwq/QQListener",
        IssuesUrl = "https://github.com/alright-qwq/QQListener/issues"
    };

    /// <summary>从 manifest 字段构造。Url 为空时 IssuesUrl 也为空，由 UI 用 ProviderAvailable 控制可点。</summary>
    public static QqListenerPluginInfo FromManifest(string version, string author, string? url)
    {
        var safeUrl = url ?? "";
        var trimmed = safeUrl.TrimEnd('/');
        var issues = string.IsNullOrWhiteSpace(trimmed) ? "" : $"{trimmed}/issues";
        return new QqListenerPluginInfo
        {
            Version = version ?? "",
            Author = author ?? "",
            Url = safeUrl,
            IssuesUrl = issues
        };
    }
}
```

- [ ] **Step 2: 构建确认编译通过**

Run: `dotnet build QQListener.sln 2>&1 | tail -10`
Expected: `0 个警告，0 个错误`（QqListenerPluginInfo 还没被任何地方使用，所以只是编译多一个文件）

- [ ] **Step 3: 提交**

```bash
git add Models/QqListenerPluginInfo.cs
git commit -m "feat: 新增 QqListenerPluginInfo 单例承载 manifest 派生元数据"
```

---

## Task 2: Plugin.Initialize 构造并注册 QqListenerPluginInfo

**Files:**
- Modify: `Plugin.cs`

- [ ] **Step 1: 改 Plugin.Initialize 头部，构造 PluginInfo 并 AddSingleton**

打开 `Plugin.cs`，在现有 `services.AddSingleton(diagnostics);` 这一行前后做修改。当前的 Initialize 长这样（参考已修改后的版本）：

```csharp
public override void Initialize(HostBuilderContext context, IServiceCollection services)
{
    var settings = QqListenerSettings.Load(PluginConfigFolder);
    settings.Normalize();
    TrySaveSettings(settings);

    var diagnostics = new QqListenerDiagnosticsState();
    var assemblyPath = Assembly.GetExecutingAssembly().Location;
    diagnostics.RecordRuntimeInfo(
        Info.Manifest.Version?.ToString() ?? "",
        Info.PluginFolderPath,
        settings.SettingsFilePath,
        assemblyPath,
        File.Exists(assemblyPath) ? File.GetLastWriteTime(assemblyPath) : null);

    services.AddSingleton(settings);
    ...
    services.AddSingleton(diagnostics);
    ...
}
```

在构造 `diagnostics` 之后、`services.AddSingleton(diagnostics);` 那一片附近加：

```csharp
var pluginInfo = QqListenerPluginInfo.FromManifest(
    Info.Manifest.Version ?? "",
    Info.Manifest.Author ?? "",
    Info.Manifest.Url);

services.AddSingleton(pluginInfo);
```

为了清晰，整段顺序调成：

```csharp
public override void Initialize(HostBuilderContext context, IServiceCollection services)
{
    var settings = QqListenerSettings.Load(PluginConfigFolder);
    settings.Normalize();
    TrySaveSettings(settings);

    var diagnostics = new QqListenerDiagnosticsState();
    var assemblyPath = Assembly.GetExecutingAssembly().Location;
    diagnostics.RecordRuntimeInfo(
        Info.Manifest.Version ?? "",
        Info.PluginFolderPath,
        settings.SettingsFilePath,
        assemblyPath,
        File.Exists(assemblyPath) ? File.GetLastWriteTime(assemblyPath) : null);

    var pluginInfo = QqListenerPluginInfo.FromManifest(
        Info.Manifest.Version ?? "",
        Info.Manifest.Author ?? "",
        Info.Manifest.Url);

    services.AddSingleton(settings);
    services.AddSingleton<WindowsNotificationReader>();
    services.AddSingleton<QqToastMessageParser>();
    services.AddSingleton<MessageRuleEvaluator>();
    services.AddSingleton<MessageEventPipeline>();
    services.AddSingleton<ClassIslandMessageNotifier>();
    services.AddSingleton<LatestMessageState>();
    services.AddSingleton(diagnostics);
    services.AddSingleton(pluginInfo);

    services.AddNotificationProvider<QqNotificationProvider>();
    services.AddComponent<LatestQqMessageComponent, LatestQqMessageComponentSettingsControl>();
    RegisterBaseActions(services);
    RegisterActionMenu();

    services.AddSettingsPage<QqListenerSettingsPage>();
}
```

注意：`Info.Manifest.Version` 已经是 `string`，原代码 `Version?.ToString()` 是冗余调用 — 顺手简化为 `Version ?? ""`。

- [ ] **Step 2: 构建确认**

Run: `dotnet build QQListener.sln 2>&1 | tail -10`
Expected: `0 个警告，0 个错误`

- [ ] **Step 3: 提交**

```bash
git add Plugin.cs
git commit -m "feat: 在 Plugin.Initialize 中构造并注册 QqListenerPluginInfo"
```

---

## Task 3: QqNotificationProvider 暴露 ResetDetectionState 与 PollOnceManuallyAsync

**Files:**
- Modify: `Services/QqNotificationProvider.cs`

- [ ] **Step 1: 把 ResetDetectionState 从 private 改为 public**

打开 `Services/QqNotificationProvider.cs`，找到：

```csharp
    private void ResetDetectionState()
    {
        lock (_pollLock)
        {
            _knownNotificationKeys.Clear();
            _pipeline.ClearState();
            _hasInitializedNotifications = false;
        }
    }
```

改成：

```csharp
    /// <summary>
    /// 清空去重 / 冷却 / 已记忆通知集合，并把"初始化完成"标志重置。
    /// 既被 IsEnabled 切回 true 时复用，也被设置页的"重置去重"按钮直接调用。
    /// </summary>
    public void ResetDetectionState()
    {
        lock (_pollLock)
        {
            _knownNotificationKeys.Clear();
            _pipeline.ClearState();
            _hasInitializedNotifications = false;
        }
    }
```

- [ ] **Step 2: 新增 PollOnceManuallyAsync**

在 `PollOnceAsync` 方法之后（也就是文件里 `private IEnumerable<MessageEvent> ParseVisibleEvents` 那个方法之前的合适位置）加：

```csharp
    /// <summary>
    /// 让外部（设置页"手动扫一次"按钮）排队执行一次轮询。
    /// 不绕开 _pollLock —— 直接 await PollOnceAsync()，靠它自己内部的锁与后台轮询串行化。
    /// IsEnabled=false 时 PollOnceAsync 自身会 early-return，这里不做重复判断。
    /// </summary>
    public Task PollOnceManuallyAsync()
    {
        // 若 listener 还没启动（StartAsync 没跑），后台 token 为空，照样能跑：
        // PollOnceAsync 是无参数的私有方法，不依赖 cancellationToken。
        return PollOnceAsync();
    }
```

- [ ] **Step 3: 构建确认 + 跑样例**

Run: `dotnet build QQListener.sln 2>&1 | tail -10`
Expected: `0 个警告，0 个错误`

Run: `dotnet run --no-build --project Samples/QQListener.SampleVerifier/QQListener.SampleVerifier.csproj`
Expected: `Verified 23 QQ toast samples.`

- [ ] **Step 4: 提交**

```bash
git add Services/QqNotificationProvider.cs
git commit -m "feat: QqNotificationProvider 暴露 ResetDetectionState 与 PollOnceManuallyAsync"
```

---

## Task 4: QqListenerSettingsPage code-behind 接入 PluginInfo 与按钮状态

**Files:**
- Modify: `Views/SettingsPages/QqListenerSettingsPage.cs`

- [ ] **Step 1: 引入 namespace 并改构造函数 / 新增属性**

打开 `Views/SettingsPages/QqListenerSettingsPage.cs`。当前文件长这样：

```csharp
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using QQListener.Models;

namespace QQListener.Views.SettingsPages;

[SettingsPageInfo("qqlistener.settings", "QQListener", "", "")]
public partial class QqListenerSettingsPage : SettingsPageBase
{
    private readonly QqListenerSettings _settings;
    private readonly QqListenerDiagnosticsState _diagnostics;
    private readonly bool _autoSaveEnabled;
    private DispatcherTimer? _saveTimer;

    public QqListenerSettings Settings => _settings;
    public QqListenerDiagnosticsState Diagnostics => _diagnostics;

    public QqListenerSettingsPage() : this(new QqListenerSettings(), new QqListenerDiagnosticsState(), false)
    {
        _diagnostics.RecordRuntimeInfo("设计器", "未通过插件宿主加载", "", GetType().Assembly.Location, null);
    }

    public QqListenerSettingsPage(QqListenerSettings settings, QqListenerDiagnosticsState diagnostics)
        : this(settings, diagnostics, true) { }
    ...
}
```

把它替换成下面的完整版本（保留所有现有逻辑，只新增 PluginInfo / Provider 引用 / 按钮属性 / handler）：

```csharp
using System.ComponentModel;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QQListener.Models;
using QQListener.Services;

namespace QQListener.Views.SettingsPages;

[SettingsPageInfo("qqlistener.settings", "QQListener", "", "")]
public partial class QqListenerSettingsPage : SettingsPageBase, INotifyPropertyChanged
{
    private readonly QqListenerSettings _settings;
    private readonly QqListenerDiagnosticsState _diagnostics;
    private readonly QqListenerPluginInfo _pluginInfo;
    private readonly bool _autoSaveEnabled;
    private DispatcherTimer? _saveTimer;
    private bool _providerAvailableCache;
    private bool _providerProbed;

    public QqListenerSettings Settings => _settings;
    public QqListenerDiagnosticsState Diagnostics => _diagnostics;
    public QqListenerPluginInfo PluginInfo => _pluginInfo;

    /// <summary>是否能找到 QqNotificationProvider 实例，决定快捷按钮是否可点。</summary>
    public bool ProviderAvailable
    {
        get
        {
            if (!_providerProbed)
            {
                _providerProbed = true;
                _providerAvailableCache = TryGetProvider() != null;
            }
            return _providerAvailableCache;
        }
    }

    public bool CanPoll => ProviderAvailable && _settings.IsEnabled;
    public bool CanReplay => ProviderAvailable && _settings.IsEnabled;

    public new event PropertyChangedEventHandler? PropertyChanged;

    public QqListenerSettingsPage()
        : this(new QqListenerSettings(), new QqListenerDiagnosticsState(), QqListenerPluginInfo.Designer, false)
    {
        _diagnostics.RecordRuntimeInfo("设计器", "未通过插件宿主加载", "", GetType().Assembly.Location, null);
    }

    public QqListenerSettingsPage(
        QqListenerSettings settings,
        QqListenerDiagnosticsState diagnostics,
        QqListenerPluginInfo pluginInfo)
        : this(settings, diagnostics, pluginInfo, true)
    {
    }

    private QqListenerSettingsPage(
        QqListenerSettings settings,
        QqListenerDiagnosticsState diagnostics,
        QqListenerPluginInfo pluginInfo,
        bool autoSaveEnabled)
    {
        _settings = settings;
        _diagnostics = diagnostics;
        _pluginInfo = pluginInfo;
        _autoSaveEnabled = autoSaveEnabled;
        DataContext = this;
        InitializeComponent();

        if (_autoSaveEnabled)
        {
            _settings.PropertyChanged += OnSettingsPropertyChanged;
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QqListenerSettings.IsEnabled))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanPoll)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanReplay)));
        }
        DebounceSave();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_autoSaveEnabled)
        {
            _settings.PropertyChanged -= OnSettingsPropertyChanged;
        }
        if (_saveTimer != null)
        {
            _saveTimer.Stop();
            _saveTimer.Tick -= OnSaveTimerTick;
            _saveTimer = null;
        }
    }

    private void DebounceSave()
    {
        if (_saveTimer == null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveTimer.Tick += OnSaveTimerTick;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnSaveTimerTick(object? sender, EventArgs e)
    {
        _saveTimer?.Stop();
        if (_settings.SuppressAutoSave) return;
        _settings.Normalize();
        _settings.Save();
    }

    // ===== 快捷操作 按钮 handlers =====

    private async void OnPollNowClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var provider = TryGetProvider();
            if (provider == null) return;
            await provider.PollOnceManuallyAsync();
        }
        catch (Exception ex)
        {
            _diagnostics.RecordError(ex, "手动扫一次失败");
        }
    }

    private void OnResetClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var provider = TryGetProvider();
            if (provider == null) return;
            provider.ResetDetectionState();
            _diagnostics.RecordIgnored("已手动重置去重与冷却状态");
        }
        catch (Exception ex)
        {
            _diagnostics.RecordError(ex, "重置去重失败");
        }
    }

    private void OnReplayLatestClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var provider = TryGetProvider();
            if (provider == null) return;

            var latest = IAppHost.GetService<LatestMessageState>();
            if (latest is not { HasMessage: true }) return; // 静默：无消息不弹

            var message = new MessageEvent(
                "qq.toast",
                string.IsNullOrWhiteSpace(latest.Source) ? "QQ" : latest.Source,
                latest.Sender,
                latest.Conversation,
                latest.Message,
                latest.ReceivedAt,
                latest.Importance,
                [latest.Sender, latest.Message],
                $"qq.toast:replay:{latest.Sender}");
            provider.ShowQqNotification(message);
        }
        catch (Exception ex)
        {
            _diagnostics.RecordError(ex, "重示最后一条失败");
        }
    }

    private static QqNotificationProvider? TryGetProvider()
    {
        var services = IAppHost.Host?.Services;
        if (services == null) return null;
        return services.GetService<QqNotificationProvider>()
               ?? services.GetServices<IHostedService>().OfType<QqNotificationProvider>().FirstOrDefault();
    }
}
```

注意几个关键点：
- **`new event PropertyChangedEventHandler? PropertyChanged`** 使用 `new` 隐藏基类（`SettingsPageBase`/`ObservableObject` 可能已有同名事件），手动 raise `CanPoll`/`CanReplay` 变更；这避免侵入 Avalonia 基类的属性通知机制。
- `ProviderAvailable` 用懒求值 + 缓存。它在初次 `Binding` 求值时探测，所以页头加载完毕时 provider 通常已经就绪。如果探测时还没就绪（启动早期），快捷区按钮就是 disabled，重新打开设置页会重新探测。
- `OnReplayLatestClick` 在 `HasMessage=false` 时静默——与 `ShowLatestMessageAction` 的行为对齐。
- handler 全用 try/catch 包住，把异常路由到诊断的"最近错误"行。

- [ ] **Step 2: 构建确认（XAML 还没改，可能有按钮事件未绑定警告，先看编译）**

Run: `dotnet build QQListener.sln 2>&1 | tail -15`
Expected: `0 个警告，0 个错误`。新 handler 还没被 XAML 引用，编译应该过；新构造函数若没人调用也只是 dead code（下一个 Task 会接上）。

- [ ] **Step 3: 提交**

```bash
git add Views/SettingsPages/QqListenerSettingsPage.cs
git commit -m "feat: 设置页 code-behind 加入 PluginInfo 与快捷操作 handler"
```

---

## Task 5: 改 axaml — 页头加版本号 + 新增快捷操作区

**Files:**
- Modify: `Views/SettingsPages/QqListenerSettingsPage.axaml`

- [ ] **Step 1: 在页头 tagline 之后加版本号**

打开 `Views/SettingsPages/QqListenerSettingsPage.axaml`，找到现在的页头：

```xaml
<!-- Page Header -->
<StackPanel Orientation="Horizontal" Margin="0,0,0,12">
    <ci:FluentIcon FontSize="48"
        Glyph="&#xE02B;"
        Foreground="{DynamicResource TextFillColorSecondaryBrush}"
        VerticalAlignment="Center"
        Margin="0,0,12,0" />
    <StackPanel VerticalAlignment="Center">
        <TextBlock FontSize="24" FontWeight="SemiBold">QQListener</TextBlock>
        <TextBlock FontSize="13"
                   Foreground="{DynamicResource TextFillColorTertiaryBrush}">
            监听 QQ 通知并转发为 ClassIsland 提醒
        </TextBlock>
    </StackPanel>
</StackPanel>
```

在 tagline TextBlock 后面追加一行版本号 TextBlock：

```xaml
<!-- Page Header -->
<StackPanel Orientation="Horizontal" Margin="0,0,0,12">
    <ci:FluentIcon FontSize="48"
        Glyph="&#xE02B;"
        Foreground="{DynamicResource TextFillColorSecondaryBrush}"
        VerticalAlignment="Center"
        Margin="0,0,12,0" />
    <StackPanel VerticalAlignment="Center">
        <TextBlock FontSize="24" FontWeight="SemiBold">QQListener</TextBlock>
        <TextBlock FontSize="13"
                   Foreground="{DynamicResource TextFillColorTertiaryBrush}">
            监听 QQ 通知并转发为 ClassIsland 提醒
        </TextBlock>
        <TextBlock FontSize="12"
                   Foreground="{DynamicResource TextFillColorTertiaryBrush}"
                   Text="{Binding PluginInfo.HeaderLine}" />
    </StackPanel>
</StackPanel>
```

- [ ] **Step 2: 在页头之后、"基础设置"之前插入快捷操作区**

紧接着页头 StackPanel 的闭合标签 `</StackPanel>` 之后、`<!-- 基础设置 -->` 注释之前，插入：

```xaml
<!-- 快捷操作 -->
<TextBlock FontSize="18"
           FontWeight="SemiBold"
           Foreground="{DynamicResource TextFillColorTertiaryBrush}"
           Margin="0,12,0,0">
    快捷操作
</TextBlock>

<StackPanel Orientation="Horizontal" Spacing="8" Margin="0,4,0,4">
    <Button Click="OnPollNowClick" IsEnabled="{Binding CanPoll}">
        <StackPanel Orientation="Horizontal" Spacing="6">
            <ci:FluentIcon Glyph="&#xE72C;" />
            <TextBlock Text="手动扫一次" />
        </StackPanel>
    </Button>
    <Button Click="OnResetClick" IsEnabled="{Binding ProviderAvailable}">
        <StackPanel Orientation="Horizontal" Spacing="6">
            <ci:FluentIcon Glyph="&#xE71D;" />
            <TextBlock Text="重置去重" />
        </StackPanel>
    </Button>
    <Button Click="OnReplayLatestClick" IsEnabled="{Binding CanReplay}">
        <StackPanel Orientation="Horizontal" Spacing="6">
            <ci:FluentIcon Glyph="&#xE0A5;" />
            <TextBlock Text="重示最后一条" />
        </StackPanel>
    </Button>
</StackPanel>
```

- [ ] **Step 3: 构建确认**

Run: `dotnet build QQListener.sln 2>&1 | tail -15`
Expected: `0 个警告，0 个错误`。如果出现 `OnPollNowClick` 未找到错误，说明 Task 4 的 code-behind 没合进来 —— 回滚检查。

- [ ] **Step 4: 提交**

```bash
git add Views/SettingsPages/QqListenerSettingsPage.axaml
git commit -m "feat: 设置页加版本号小字与快捷操作三按钮"
```

---

## Task 6: 合并诊断区前 4 行为可展开"运行环境"项

**Files:**
- Modify: `Views/SettingsPages/QqListenerSettingsPage.axaml`

- [ ] **Step 1: 删掉 4 条独立 SettingsExpander，替换成可展开项**

打开 axaml，找到诊断区开头的这 4 个 SettingsExpander：

```xaml
<controls:SettingsExpander IconSource="{ci:FluentIconSource Glyph=&#xE895;}"
                           Header="插件构建"
                           Description="{Binding Diagnostics.RuntimeSummary}" />

<controls:SettingsExpander IconSource="{ci:FluentIconSource Glyph=&#xE8B7;}"
                           Header="插件目录"
                           Description="{Binding Diagnostics.PluginFolderPath}" />

<controls:SettingsExpander IconSource="{ci:FluentIconSource Glyph=&#xE8A5;}"
                           Header="当前 DLL"
                           Description="{Binding Diagnostics.AssemblyPath}" />

<controls:SettingsExpander IconSource="{ci:FluentIconSource Glyph=&#xE713;}"
                           Header="配置文件"
                           Description="{Binding Diagnostics.SettingsPath}" />
```

替换为单个可展开的 SettingsExpander（保留"插件构建"那个图标）：

```xaml
<controls:SettingsExpander IconSource="{ci:FluentIconSource Glyph=&#xE895;}"
                           Header="运行环境"
                           Description="{Binding Diagnostics.RuntimeSummary}"
                           IsExpanded="False">
    <controls:SettingsExpander.Items>
        <controls:SettingsExpanderItem IconSource="{ci:FluentIconSource Glyph=&#xE8B7;}"
                                       Content="插件目录">
            <controls:SettingsExpanderItem.Footer>
                <SelectableTextBlock Text="{Binding Diagnostics.PluginFolderPath}"
                                     FontSize="12"
                                     Foreground="{DynamicResource TextFillColorSecondaryBrush}"
                                     MaxWidth="380"
                                     TextWrapping="Wrap" />
            </controls:SettingsExpanderItem.Footer>
        </controls:SettingsExpanderItem>

        <controls:SettingsExpanderItem IconSource="{ci:FluentIconSource Glyph=&#xE8A5;}"
                                       Content="当前 DLL">
            <controls:SettingsExpanderItem.Footer>
                <SelectableTextBlock Text="{Binding Diagnostics.AssemblyPath}"
                                     FontSize="12"
                                     Foreground="{DynamicResource TextFillColorSecondaryBrush}"
                                     MaxWidth="380"
                                     TextWrapping="Wrap" />
            </controls:SettingsExpanderItem.Footer>
        </controls:SettingsExpanderItem>

        <controls:SettingsExpanderItem IconSource="{ci:FluentIconSource Glyph=&#xE713;}"
                                       Content="配置文件">
            <controls:SettingsExpanderItem.Footer>
                <SelectableTextBlock Text="{Binding Diagnostics.SettingsPath}"
                                     FontSize="12"
                                     Foreground="{DynamicResource TextFillColorSecondaryBrush}"
                                     MaxWidth="380"
                                     TextWrapping="Wrap" />
            </controls:SettingsExpanderItem.Footer>
        </controls:SettingsExpanderItem>
    </controls:SettingsExpander.Items>
</controls:SettingsExpander>
```

诊断区其他 8 条 SettingsExpander（监听状态/通知权限/AppInfo 状态/最近读取/QQOnly 筛选/最近接收/最近忽略/最近错误）**保持原样**。

- [ ] **Step 2: 构建确认**

Run: `dotnet build QQListener.sln 2>&1 | tail -10`
Expected: `0 个警告，0 个错误`

- [ ] **Step 3: 提交**

```bash
git add Views/SettingsPages/QqListenerSettingsPage.axaml
git commit -m "feat: 诊断区路径四行合并为可展开的运行环境项"
```

---

## Task 7: 关于区卡片

**Files:**
- Modify: `Views/SettingsPages/QqListenerSettingsPage.axaml`

- [ ] **Step 1: 在文件末尾、`</StackPanel></ScrollViewer>` 之前插入关于区**

打开 axaml，找到最末尾的：

```xaml
            <controls:SettingsExpander IconSource="{ci:FluentIconSource Glyph=&#xEB90;}"
                                       Header="最近错误"
                                       Description="{Binding Diagnostics.LastErrorSummary}" />

        </StackPanel>
    </ScrollViewer>
</ci:SettingsPageBase>
```

在"最近错误" SettingsExpander 闭合之后、`</StackPanel>` 闭合之前，插入关于区：

```xaml
<!-- 关于 -->
<TextBlock FontSize="18"
           FontWeight="SemiBold"
           Foreground="{DynamicResource TextFillColorTertiaryBrush}"
           Margin="0,20,0,0">
    关于
</TextBlock>

<Border CornerRadius="4"
        ClipToBounds="True"
        Margin="0,4,0,4">
    <controls:SettingsExpanderItem>
        <Grid Margin="12" ColumnDefinitions="Auto,*,Auto">
            <ci:FluentIcon Glyph="&#xE02B;"
                           Grid.Column="0"
                           FontSize="48"
                           Foreground="{DynamicResource TextFillColorSecondaryBrush}"
                           VerticalAlignment="Center" />

            <StackPanel Grid.Column="1"
                        Margin="12,0,0,0"
                        VerticalAlignment="Center">
                <TextBlock>
                    <Run Text="QQListener  " FontSize="20" FontWeight="SemiBold" />
                    <Run Text="{Binding PluginInfo.Version, StringFormat=v{0}}"
                         FontSize="14"
                         Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
                </TextBlock>
                <TextBlock FontSize="12"
                           Foreground="{DynamicResource TextFillColorSecondaryBrush}"
                           Text="{Binding PluginInfo.CopyrightLine}" />
                <TextBlock FontSize="11"
                           Foreground="{DynamicResource TextFillColorTertiaryBrush}"
                           Text="{Binding PluginInfo.LicenseLine}" />
                <TextBlock FontSize="11"
                           Foreground="{DynamicResource TextFillColorTertiaryBrush}"
                           Text="{Binding PluginInfo.SdkLine}" />
                <TextBlock FontSize="11"
                           Foreground="{DynamicResource TextFillColorTertiaryBrush}"
                           Text="{Binding PluginInfo.OriginalAuthor}" />
            </StackPanel>

            <StackPanel Grid.Column="2"
                        Orientation="Horizontal"
                        Spacing="4"
                        VerticalAlignment="Top">
                <Button Padding="8,5"
                        BorderBrush="Transparent"
                        Background="Transparent"
                        ToolTip.Tip="GitHub 仓库"
                        Command="{x:Static ci:UriNavigationCommands.UriNavigationCommand}"
                        CommandParameter="{Binding PluginInfo.Url}">
                    <ci:FluentIcon Glyph="&#xE71B;" FontSize="18" />
                </Button>
                <Button Padding="8,5"
                        BorderBrush="Transparent"
                        Background="Transparent"
                        ToolTip.Tip="常见问题 Q&amp;A"
                        Command="{x:Static ci:UriNavigationCommands.UriNavigationCommand}"
                        CommandParameter="{Binding PluginInfo.QaUrl}">
                    <ci:FluentIcon Glyph="&#xE897;" FontSize="18" />
                </Button>
                <Button Padding="8,5"
                        BorderBrush="Transparent"
                        Background="Transparent"
                        ToolTip.Tip="报告问题"
                        Command="{x:Static ci:UriNavigationCommands.UriNavigationCommand}"
                        CommandParameter="{Binding PluginInfo.IssuesUrl}">
                    <ci:FluentIcon Glyph="&#xEA39;" FontSize="18" />
                </Button>
            </StackPanel>
        </Grid>
    </controls:SettingsExpanderItem>
</Border>
```

图标说明（FluentIcon glyph 含义）：
- `` — 邮件/消息（与页头一致，呼应品牌）
- `` — 链接（GitHub）
- `` — 问号/帮助（Q&A）
- `` — bug/问题（Issues）

`&amp;` 是 XAML 里的 `&` 转义，必须写 `Q&amp;A` 否则 XAML 解析失败。

- [ ] **Step 2: 构建确认**

Run: `dotnet build QQListener.sln 2>&1 | tail -10`
Expected: `0 个警告，0 个错误`

- [ ] **Step 3: 跑样例确认无回归**

Run: `dotnet run --no-build --project Samples/QQListener.SampleVerifier/QQListener.SampleVerifier.csproj`
Expected: `Verified 23 QQ toast samples.`

- [ ] **Step 4: 提交**

```bash
git add Views/SettingsPages/QqListenerSettingsPage.axaml
git commit -m "feat: 设置页底部新增关于卡片含版本/许可/外链"
```

---

## Task 8: 端到端冒烟（开发者手动验证 + 截图友好检查）

**Files:**
- 无文件改动。这是一个本地运行验证 task。

- [ ] **Step 1: 启动 ClassIsland 加载本地构建**

由于 csproj 的 `SyncLocalClassIslandDebugPlugin` 目标，刚才几次 `dotnet build` 已经把 dll 同步到了 `..\ClassIsland\ClassIsland.Desktop\bin\Debug\...\Plugins\QQListener\`。如果本机 ClassIsland.Desktop 已经运行，需要先关闭再启动一次：

```bash
# 用户操作：关闭 ClassIsland，再启动它
```

- [ ] **Step 2: 打开 QQListener 设置页，按 spec 第 11 节的清单逐项验证**

预期（参考 spec《测试与验证》一节）：

1. **页头第三行**显示 `v1.0.1.0 · by alright-qwq`（或当前 manifest.yml 里的 version）。
2. **快捷操作区**三个按钮在 IsEnabled=true 时都可点；把"启用监听"关掉后，"手动扫一次"和"重示最后一条"灰掉，"重置去重"仍可点。
3. **点"手动扫一次"**，诊断区"最近读取"时间戳更新（看时间戳是新值）。
4. **点"重置去重"**，诊断区"最近忽略"显示 `已手动重置去重与冷却状态`。
5. **点"重示最后一条"**：有最近消息时 ClassIsland 弹出对应提醒；没消息时什么都不弹（也不报错）。
6. **诊断"运行环境"折叠**时显示版本+构建时间；展开后三条路径完整可见、可选中。
7. **关于区**点 GitHub/Q&A/Issues 三个按钮，ClassIsland 弹外链确认对话框，URL 正确（GitHub=manifest.url，Q&A=al-right.top/archives/58，Issues=manifest.url/issues）。

如果**任何一项不通过**：回到对应 Task 修复。

- [ ] **Step 3: 提交一条记录性 commit（可选）**

如果手动验证全过，记录一条空提交标记里程碑：

```bash
git commit --allow-empty -m "test: 设置页丰富化完成端到端冒烟"
```

---

## 验收

完整执行后应满足：

- [x] `dotnet build QQListener.sln` 0 警告 0 错误
- [x] `dotnet run --project Samples/QQListener.SampleVerifier` 输出 `Verified 23 QQ toast samples.`
- [x] 设置页页头有 `v{version} · by {author}` 小字
- [x] 设置页页头之下有 3 个快捷操作按钮，按 spec 描述工作
- [x] 诊断区前 4 条路径行合并为可展开的"运行环境"项
- [x] 设置页底部有"关于"卡片，含版本/Copyright/License/SDK/原作者/3 个外链按钮

---

## 风险与降级路径

- **`OnReplayLatestClick` 里通过 `IAppHost.GetService<LatestMessageState>()` 拿状态**：和 `ShowLatestMessageAction` 行为一致。如果设计器场景下 `IAppHost.Host` 为 null，handler 的 `TryGetProvider()` 返回 null 提前 return，不会 NPE。
- **`ProviderAvailable` 懒探测**：若用户首次打开设置页时 hosted services 还没起来（启动期），按钮一直 disabled。下次打开会重新探测。如果体验差，可以改成定时器轮询，但目前 ClassIsland 加载流程下基本不会触发。
- **`PollOnceManuallyAsync` 重入**：`PollOnceAsync` 内部用 `_pollLock` 串行化，重入会等待。但 button click handler 是 async void，连点会排队多次执行。如果排查问题时不想要这种行为，可以加一个 `_isPolling` 标志，但 spec 没要求，目前不做。

---

## YAGNI 边界（spec 明确不做）

- 不做"打开插件目录"按钮
- 不做更新日志 Tab / 插件介绍 Tab / 插件市场按钮
- 不做"清空最近消息"快捷按钮
- 不引入 Markdown 渲染器
