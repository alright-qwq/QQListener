# ClassIsland 插件开发基本用法

> 基于 ClassIsland SDK 2.0、示例插件及 QQListener 项目的整理。

---

## 1. 项目结构

一个最小的 ClassIsland 插件项目包含：

```
MyPlugin/
  MyPlugin.csproj          # 项目文件
  Plugin.cs                # 插件入口
  manifest.yml             # 插件清单
  icon.png                 # 插件图标
  README.md                # 说明文档
```

### csproj 关键配置

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>   <!-- 必须：插件隔离加载 -->
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- SDK 引用：排除 runtime 和 native，宿主已提供 -->
    <PackageReference Include="ClassIsland.PluginSdk" Version="1.7.106.2-dev-v2">
      <ExcludeAssets>runtime; native</ExcludeAssets>
    </PackageReference>
  </ItemGroup>

  <!-- 必需的内容文件 -->
  <ItemGroup>
    <Content Include="manifest.yml" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="README.md" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="icon.png" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

---

## 2. manifest.yml

```yaml
id: QQListener                          # 唯一插件 ID
name: QQListener                        # 显示名称
description: 监听 QQ 通知并转发为 ClassIsland 提醒
entranceAssembly: "QQListener.dll"       # 包含 [PluginEntrance] 的程序集
url: https://github.com/...             # 项目地址
version: 0.1.0.0                        # 插件版本
apiVersion: 2.0.0.0                     # 目标 API 版本
author: your-name                       # 作者
```

可选字段：`icon`、`readme`、`dependencies`（依赖插件列表）、`supportedOSPlatforms`。

---

## 3. 插件入口 — PluginBase

```csharp
[PluginEntrance]
public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // 所有注册在此完成
        services.AddSettingsPage<MySettingsPage>();
        services.AddNotificationProvider<MyNotificationProvider>();
        services.AddAction<MyAction>();
        // ...
    }
}
```

**可用属性：**
- `PluginConfigFolder` — 插件独立的配置目录
- `Info` — 插件元数据（`Info.Manifest.Version`、`Info.PluginFolderPath`）

---

## 4. 核心注册模式

所有注册通过 `IServiceCollection` 扩展方法完成，遵循统一模式：**用 Attribute 标注类 → 在 Initialize 中调用对应扩展方法**。

### 4.1 设置页面

```csharp
[SettingsPageInfo("myplugin.settings", "MyPlugin设置", IconGlyph = "\uE713")]
public partial class MySettingsPage : SettingsPageBase
{
    public MySettingsPage() => InitializeComponent();
}

// 注册
services.AddSettingsPage<MySettingsPage>();

// 可选：设置页分组
services.AddSettingsPageGroup("mygroup", "\uE700", "我的插件");
```

### 4.2 通知提供者

```csharp
[NotificationProviderInfo("a6f94b7a-d398-41d4-b3c7-208fb9d9ad7b", "MyProvider", "\uE715")]
public class MyNotificationProvider : NotificationProviderBase, IHostedService
{
    // IHostedService: StartAsync / StopAsync 管理生命周期
    // 调用 ShowNotification(NotificationRequest) 显示通知
}

// 注册
services.AddNotificationProvider<MyNotificationProvider>();
```

- 泛型版本 `NotificationProviderBase<TSettings>` 自动加载强类型设置
- 实现 `IHostedService` 后，DI 容器自动管理启动/停止

### 4.3 自动化操作 (Action)

```csharp
[ActionInfo("myplugin.action.do", "执行操作", "\uE715")]
public class MyAction : ActionBase
{
    public override void OnInvoke() { /* 执行 */ }
    public override void OnRevert() { /* 撤销 */ }  // 可选
}

// 注册
services.AddAction<MyAction>();
```

- `OnRevert()` 用于自动化规则退出时还原状态
- `IsRevertable` 属性控制是否支持撤销

### 4.4 自动化触发器 (Trigger)

```csharp
[TriggerInfo("myplugin.trigger.event", "某事件", "\uE700")]
public class MyTrigger : TriggerBase
{
    public override void Loaded() { /* 订阅事件 */ }
    public override void UnLoaded() { /* 取消订阅 */ }
}

// 注册
services.AddTrigger<MyTrigger>();
```

### 4.5 主界面组件 (Component)

```csharp
[ComponentInfo("guid-string", "组件名称", "\uE700")]
public class MyComponent : ComponentBase
{
}

// 注册
services.AddComponent<MyComponent>();
```

### 4.6 通用单例服务

```csharp
services.AddSingleton<MyService>();
services.AddHostedService<MyBackgroundService>();
```

---

## 5. 属性标注速查表

| Attribute | 标记目标 | 关键参数 |
|---|---|---|
| `[PluginEntrance]` | 插件入口类 | 无（标记） |
| `[SettingsPageInfo]` | 设置页 | `id`, `name`, `IconGlyph`, `Category` |
| `[NotificationProviderInfo]` | 通知提供者 | `Guid`, `name`, `IconGlyph` |
| `[ActionInfo]` | 自动化操作 | `id`, `name`, `IconGlyph`, `IsRevertable` |
| `[TriggerInfo]` | 自动化触发器 | `id`, `name`, `IconGlyph` |
| `[ComponentInfo]` | 主界面组件 | `Guid`, `name`, `IconGlyph` |

---

## 6. 配置/设置模式

### 设置类定义

```csharp
public partial class MySettings : ObservableObject
{
    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _someText = "";

    // JSON 序列化/反序列化
    public static MySettings Load(string directory)
    {
        var path = Path.Combine(directory, "settings.json");
        if (!File.Exists(path)) return new MySettings();
        return JsonSerializer.Deserialize<MySettings>(File.ReadAllText(path)) ?? new();
    }

    public void Save()
    {
        var path = Path.Combine(SettingsDirectory, "settings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
```

### 在入口中注册为单例实例

```csharp
public override void Initialize(HostBuilderContext context, IServiceCollection services)
{
    var settings = MySettings.Load(PluginConfigFolder);
    services.AddSingleton(settings);  // 注册实例，非类型
    // ... 其他服务通过 DI 获取同一实例
}
```

### 设置页面保存防抖

```csharp
public partial class MySettingsPage : SettingsPageBase
{
    private readonly DispatcherTimer _saveTimer;
    private readonly MySettings _settings;

    public MySettingsPage(MySettings settings)
    {
        _settings = settings;
        DataContext = this;
        InitializeComponent();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) =>
        {
            _settings.Save();
            _saveTimer.Stop();
        };
        settings.PropertyChanged += (_, _) => { _saveTimer.Stop(); _saveTimer.Start(); };
    }
}
```

---

## 7. Windows 通知监听（QQListener 核心模式）

### 7.1 整体数据流

```
Windows Toast 通知
  → WindowsNotificationReader (WinRT API 读取)
  → QqNotificationProvider (轮询 + 过滤)
  → QqMessageProcessor (去重 + 分类)
  → QqNotificationMessage (DTO)
  → ShowNotification() → ClassIsland 通知系统
```

### 7.2 WindowsRuntime API 读取通知

```csharp
using Windows.UI.Notifications.Management;

public async Task<IReadOnlyList<WindowsToastSnapshot>> ReadToastNotificationsAsync()
{
    var listener = UserNotificationListener.Current;
    var access = await listener.RequestAccessAsync();
    if (access != UserNotificationListenerAccessStatus.Allowed)
        return [];

    var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
    return notifications.Select(ReadSnapshot).Where(s => s.Texts.Count > 0).ToList();
}
```

要求：`TargetFramework` 设为 `net8.0-windows10.0.19041.0`，用户需授权通知访问权限。

### 7.3 轮询架构

```csharp
// QqNotificationProvider 实现 IHostedService
public async Task StartAsync(CancellationToken cancellationToken)
{
    _listenerCts = new CancellationTokenSource();
    _ = ListenAsync(_listenerCts.Token);  // 后台运行
}

private async Task ListenAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        await PollOnceAsync();
        await Task.Delay(_settings.ScanIntervalSeconds * 1000, ct);
    }
}
```

### 7.4 去重策略（双层）

- **第一层（通知 ID）**：`QqNotificationProvider` 用 `HashSet<uint>` 追踪已见通知 ID
- **第二层（内容哈希）**：`QqMessageProcessor` 用内容哈希 + 冷却时间（`CooldownSeconds`）去重

### 7.5 消息分类优先级

```
Calling（呼叫）> Important（重要）> Normal（普通）
```

- **Calling**：消息中含 `CallingKeyword`（默认"呼叫"）
- **Important**：发送者在 `ImportantPersons` 中，或消息含 `ImportantKeywords` 关键词，或 "@我"
- **Normal**：其余消息

---

## 8. 延迟初始化

部分操作需要在 ClassIsland 完全启动后执行：

```csharp
public override void Initialize(HostBuilderContext context, IServiceCollection services)
{
    services.AddSingleton<MyService>();
    // ...

    AppBase.Current.AppStarted += async (_, _) =>
    {
        var svc = IAppHost.GetService<MyService>();
        await svc.InitializeAsync();
    };
}
```

---

## 9. 常用服务接口

通过 `IAppHost.GetService<T>()` 或构造函数注入获取：

| 接口 | 用途 |
|---|---|
| `ILessonsService` | 当前课程/课间/放学状态 |
| `INotificationHostService` | 通知队列与提供者管理 |
| `IAutomationService` | 自动化规则引擎 |
| `IActionService` | 操作执行与菜单树 |
| `IComponentsService` | 主界面组件管理 |
| `IThemeService` | 主题管理 |
| `IUriNavigationService` | URI 导航 |
| `IAudioService` | 音频播放 |
| `ISpeechService` | 语音合成 |
| `IAppLogService` | 应用日志 |

---

## 10. 实用技巧

### 条件性注册

```csharp
if (_settings.SomeFeatureEnabled)
    services.AddAction<MyFeatureAction>();
```

### 设置值范围规范化

```csharp
public void Normalize()
{
    ScanIntervalSeconds = Math.Clamp(ScanIntervalSeconds, 0.1, 60.0);
    RollingSpeed = Math.Clamp(RollingSpeed, 0.0, 4.0);
}
```

### UI 线程调度

```csharp
Avalonia.Threading.Dispatcher.UIThread.Post(() =>
{
    ShowNotification(request);
});
```

### 插件打包

构建输出 `.cipx` 文件即为可分发的插件包（标准 ZIP 格式）。

---

## 11. 参考插件复杂度对比

| 插件 | 复杂度 | 关键特征 |
|---|---|---|
| EarthquakeWarning | 低 | 单一通知提供者 + WebSocket |
| StartUpAsAdmin | 低 | 单一设置页面 |
| IslandCaller | 中 | 多服务 + 延迟初始化 + 操作注册 |
| ExtraIsland | 高 | 多组件 + 多设置页 + 条件注册 + 模块化 |
| SystemTools | 高 | 45 操作 + 条件注册 + 依赖管理 + 浮动窗口 |
| **QQListener** | **中** | 通知提供者 + WinRT + 去重 + 操作 + 设置页 |
