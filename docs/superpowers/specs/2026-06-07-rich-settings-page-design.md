# 设置页丰富化与"关于"区设计

## 目标

把 QQListener 的设置页从"只有配置 + 一长串诊断行"升级为"配置 + 快捷操作 + 关于"三段式：
入门用户能在页头一眼看到版本号；故障排查能在页头之下立刻点到"手动扫一次/重示
最后一条/重置去重"；版本号、外链、许可证集中在页底"关于"区。

这个改动只涉及单个设置页和它的运行时依赖，不动消息事件桥、不动通知提供方的核心
轮询逻辑（除了把一个 private 方法升为 public，并给外部加一个手动触发轮询的入口）。

## 范围

- `Views/SettingsPages/QqListenerSettingsPage.axaml` + `.cs`
- `Services/QqNotificationProvider.cs`（暴露两个公开 API）
- 新增 `Models/QqListenerPluginInfo.cs`（集中持有 manifest 派生的元数据）
- `Plugin.cs` 注册新单例
- 不动 `MessageEvent` / `MessageEventPipeline` / `QqToastMessageParser` 等内核

## 页面布局

最终的视觉顺序（从上到下）：

```
[页头：48px 图标 + QQListener + tagline + 小字 "v1.0.1.0 · by alright-qwq"]
[快捷操作：三个按钮]
[基础设置]   ← 不变
[时间与显示] ← 不变
[过滤规则]   ← 不变
[诊断状态]   ← 路径四行合并为一个可展开项；其他行不动
[关于]      ← 新增卡片
```

设计意图：高频操作（手动扫/重示）放在页头之下，故障排查不需要滚屏；关于区放在
页底，符合 ClassIsland 生态里 SystemTools 等参考插件的次序。

## 页头版本号

页头的 tagline 下方加一行 12px 浅色小字：

```
v1.0.1.0 · by alright-qwq
```

通过新建的 `QqListenerPluginInfo` 单例提供 `Version` 与 `Author`。设计器场景使用
"v设计器 · by alright-qwq" 作为占位，避免设计器中崩溃。

## 快捷操作

三个按钮以横向 StackPanel 排布，每个按钮左侧带 FluentIcon、右侧文字。

| 按钮 | Glyph | 行为 | 调用 |
| --- | --- | --- | --- |
| 手动扫一次 | ``（刷新） | 立刻执行一次 PollOnceAsync，结果在诊断区可见 | `provider.PollOnceManuallyAsync()` |
| 重置去重 | ``（橡皮） | 清空 `_seen`/`_activeDeduplicationKeys`/`_knownNotificationKeys`/`_hasInitializedNotifications` | `provider.ResetDetectionState()` |
| 重示最后一条 | ``（重播） | 把 LatestMessageState 重新当 MessageEvent 推给 ShowQqNotification；HasMessage=false 时静默 | code-behind 自己拼 |

按钮反馈：不弹额外 toast，只让诊断区原本就在显示的"最近读取""最近接收"自动刷新。

按钮的 IsEnabled 绑定：

- 三个按钮统一绑 `{Binding ProviderAvailable}`（设置页只读属性），值为
  `IAppHost.Host?.Services` 非 null 且能查到 `QqNotificationProvider`。
- "手动扫一次"和"重示最后一条"额外要求 `Settings.IsEnabled == true`：在 axaml
  里用一个 MultiBinding 或在 code-behind 监听 PropertyChanged 重算
  `CanPoll` / `CanReplay`。简单起见，code-behind 暴露两个布尔属性，PropertyChanged
  在 Settings.IsEnabled 变更时触发。
- "重置去重"即使 IsEnabled=false 也允许点（清状态本就是为了重新开始）。

异常处理：每个 handler 用 try/catch 包住，把异常通过 `Diagnostics.RecordError`
记到"最近错误"行，避免设置页崩溃。

## 诊断区合并

把这四条原本各占一行的 SettingsExpander：

- 插件构建（RuntimeSummary）
- 插件目录（PluginFolderPath）
- 当前 DLL（AssemblyPath）
- 配置文件（SettingsPath）

合并为单个可展开的 SettingsExpander：

- 折叠时：Header = "运行环境"，Description 绑定 `Diagnostics.RuntimeSummary`，
  显示 `1.0.1.0 | 2026-06-07 22:00:00`。
- 展开时：内含三个 `SettingsExpanderItem`，每个 Header 是中文标签（"插件目录"
  / "当前 DLL" / "配置文件"），Description 是绑定的路径。路径用 `SelectableTextBlock`
  方便用户复制。

其他诊断行（监听状态/通知权限/AppInfo 状态/最近读取/QQOnly 筛选/最近接收/最近忽略
/最近错误）保留原样、原顺序。

## 关于区

页底加一个独立卡片，模仿 SystemTools 的 About 但**不做 Tab**，只一张卡片：

```
┌─────────────────────────────────────────────────┐
│ 📩  QQListener  v1.0.1.0                        │
│     Copyright © 2026 alright-qwq                │
│     Released under the MIT License              │
│     基于 ClassIsland.PluginSdk 1.7.106.2-dev-v2 │
│     原作者：B 站 @BSOD-MEMZ（PySide6 版）         │
│                                                 │
│     [GitHub 仓库] [常见问题 Q&A] [报告问题]       │
└─────────────────────────────────────────────────┘
```

按钮触发 ClassIsland 框架自带的外链命令
`{x:Static ci:UriNavigationCommands.UriNavigationCommand}`，让宿主统一处理（弹出
确认对话框、记录历史等），不直接 `Process.Start`。

链接来源：

| 按钮 | URL | 来源 |
| --- | --- | --- |
| GitHub 仓库 | `https://github.com/alright-qwq/QQListener` | manifest.url |
| 常见问题 Q&A | `https://al-right.top/archives/58` | README.md |
| 报告问题 | `https://github.com/alright-qwq/QQListener/issues` | 在 manifest.url 后拼 `/issues` |

SDK 版本字符串：`QqListenerPluginInfo.SdkVersion` 在构造时以**硬编码常量**填入
（值 `"1.7.106.2-dev-v2"`，必须与 `QQListener.csproj` 的
`ClassIslandPluginSdkVersion` 同步维护）。理想是从 csproj 反射读出，但 SDK
当前没有公开 metadata，这里接受"两处维护"的折中，并在常量旁加注释指出唯一权威
是 csproj。后续若 SDK 暴露 `IPluginSdkInfo` 之类接口，切换到运行时读取。

## 数据流与新增类型

```
QqListenerPluginInfo (singleton, 新增)
   ├─ Version : string                  // 来自 Info.Manifest.Version
   ├─ Author : string                   // 来自 manifest.yml author 字段
   ├─ Url : string                      // 来自 Info.Manifest.Url
   ├─ IssuesUrl : string                // 拼接 Url + "/issues"
   ├─ QaUrl : string                    // 硬编码（README 里指定）
   ├─ SdkVersion : string               // 硬编码（与 csproj 同步）
   ├─ License : string                  // "MIT"
   └─ HeaderLine : string               // "v{Version} · by {Author}"

QqNotificationProvider (改动)
   ├─ Task PollOnceManuallyAsync()      // 新增：在 _listenerCts 的 token 下排队执行
   │                                    // 一次 PollOnceAsync。不绕开 _pollLock：
   │                                    // 由 PollOnceAsync 自身的锁串行化，避免和
   │                                    // 后台轮询交错。如果 listener 未启动直接返回。
   └─ void ResetDetectionState()        // private → public

QqListenerSettingsPage (改动)
   ├─ ctor(Settings, Diagnostics)               // 保留旧签名（设计器走这条）
   ├─ ctor(Settings, Diagnostics, PluginInfo)   // 新增 DI 签名
   ├─ PluginInfo : QqListenerPluginInfo         // 暴露给 XAML 绑定
   ├─ OnPollNowClick(...)                       // 按钮 handler
   ├─ OnResetClick(...)
   └─ OnReplayLatestClick(...)
```

### 为什么不直接在 Plugin.cs 里硬编码版本

manifest.yml 的版本字段是唯一权威来源。把版本/作者/URL 集中到一个单例，UI 只读，
将来发版改了 manifest 就自动同步，不会出现"manifest 升到 1.0.2.0 而设置页还显示
1.0.1.0"。这也方便测试和设计器场景给替身。

### 为什么按钮不直接复用 ShowLatestMessageAction

ShowLatestMessageAction 在 OnInvoke 里通过 IAppHost.Host.Services 反查 Provider，
这是给"自动化触发"的路径。设置页直接持有 provider 引用即可（构造期通过 code-behind
现拿 `IAppHost.GetService<QqNotificationProvider>()` + fallback hosted-services），
不需要走 Action 抽象。

## XAML 关键结构

```xaml
<ScrollViewer>
  <StackPanel Classes="settings-container animated-intro" Spacing="4">

    <!-- 页头 -->
    <StackPanel Orientation="Horizontal" Margin="0,0,0,12">
      <ci:FluentIcon Glyph="" FontSize="48" ... />
      <StackPanel VerticalAlignment="Center">
        <TextBlock FontSize="24" FontWeight="SemiBold">QQListener</TextBlock>
        <TextBlock FontSize="13" Foreground="...Tertiary...">监听 QQ 通知并转发为 ClassIsland 提醒</TextBlock>
        <TextBlock FontSize="12" Foreground="...Tertiary..." Text="{Binding PluginInfo.HeaderLine}" />
      </StackPanel>
    </StackPanel>

    <!-- 快捷操作 -->
    <TextBlock FontSize="18" FontWeight="SemiBold"
               Foreground="{DynamicResource TextFillColorTertiaryBrush}"
               Margin="0,12,0,0">快捷操作</TextBlock>
    <StackPanel Orientation="Horizontal" Spacing="8" Margin="0,4,0,4">
      <Button Click="OnPollNowClick" IsEnabled="{Binding CanPoll}">
        <StackPanel Orientation="Horizontal" Spacing="6">
          <ci:FluentIcon Glyph="&#xE72C;" /><TextBlock Text="手动扫一次" />
        </StackPanel>
      </Button>
      <Button Click="OnResetClick" IsEnabled="{Binding ProviderAvailable}">...</Button>
      <Button Click="OnReplayLatestClick" IsEnabled="{Binding CanReplay}">...</Button>
    </StackPanel>

    <!-- 现有四个区块 -->

    <!-- 诊断状态：第一项改为可展开 -->
    <controls:SettingsExpander Header="运行环境" Description="{Binding Diagnostics.RuntimeSummary}" IsExpanded="False">
      <controls:SettingsExpander.Items>
        <controls:SettingsExpanderItem Content="插件目录">
          <controls:SettingsExpanderItem.Footer>
            <SelectableTextBlock Text="{Binding Diagnostics.PluginFolderPath}" />
          </controls:SettingsExpanderItem.Footer>
        </controls:SettingsExpanderItem>
        ...
      </controls:SettingsExpander.Items>
    </controls:SettingsExpander>

    <!-- 关于 -->
    <TextBlock FontSize="18" FontWeight="SemiBold"
               Foreground="{DynamicResource TextFillColorTertiaryBrush}"
               Margin="0,20,0,0">关于</TextBlock>
    <Border CornerRadius="4" ClipToBounds="True">
      <controls:SettingsExpanderItem>
        <Grid ColumnDefinitions="Auto,*,Auto" Margin="12">
          <ci:FluentIcon Glyph="" Grid.Column="0" FontSize="48" />
          <StackPanel Grid.Column="1" Margin="12,0,0,0">
            <TextBlock>
              <Run Text="QQListener  " FontSize="20" />
              <Run Text="{Binding PluginInfo.Version, StringFormat=v{0}}" />
            </TextBlock>
            <TextBlock FontSize="12" Text="{Binding PluginInfo.CopyrightLine}" />
            <TextBlock FontSize="11" Text="{Binding PluginInfo.LicenseLine}" />
            <TextBlock FontSize="11" Text="{Binding PluginInfo.SdkLine}" />
            <TextBlock FontSize="11" Text="原作者：B 站 @BSOD-MEMZ（PySide6 版）" />
          </StackPanel>
          <StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="4">
            <Button Command="{x:Static ci:UriNavigationCommands.UriNavigationCommand}"
                    CommandParameter="{Binding PluginInfo.Url}" ToolTip.Tip="GitHub 仓库">
              <ci:FluentIcon Glyph="" />
            </Button>
            ...
          </StackPanel>
        </Grid>
      </controls:SettingsExpanderItem>
    </Border>
  </StackPanel>
</ScrollViewer>
```

## 异常与降级

- **设计器场景**：`QqListenerSettingsPage` 的无参 ctor 已经构造一个空 settings/
  空 diagnostics，新增加路径同样构造一个空 `QqListenerPluginInfo`（所有字段填
  "设计器"占位），保证设计器不崩。
- **PluginInfo 字段缺失**：所有字符串字段默认 `""`，UI 文案预留"未知"占位。
- **快捷操作按钮**：每个 handler 都用 try/catch 包住，异常路由到诊断的"最近错误"。

## 测试与验证

- 构建：`dotnet build QQListener.sln` 必须 0 警告 0 错误。
- 样例验证：`dotnet run --project Samples/QQListener.SampleVerifier`（不应回归）。
- 手动验证（启 ClassIsland 后）：
  1. 打开设置页，页头第三行看到 "v1.0.1.0 · by alright-qwq"。
  2. 点"手动扫一次"，"最近读取"行时间戳更新。
  3. 点"重置去重"，"最近忽略"应清掉旧的去重记录提示。
  4. 点"重示最后一条"：有最近消息时弹出，没消息时静默（不弹"暂无"）。
  5. 诊断"运行环境"折叠时显示版本+构建时间；展开后三条路径完整。
  6. "关于"区点 GitHub/Q&A/Issues，宿主弹外链确认对话框，URL 正确。

## YAGNI

明确**不做**的事项：

- 不做"打开插件目录"按钮（用户决定不需要）
- 不做更新日志 Tab（不像 SystemTools 那样有更新日志要露出）
- 不做"插件介绍" Tab
- 不做"插件市场"按钮（不一定每个用户都用插件市场）
- 不在快捷操作里做"清空最近消息"（与"重置去重"语义重叠且功能交叉）
- 不引入 Markdown 渲染器（关于区文案纯静态 TextBlock 即可）
