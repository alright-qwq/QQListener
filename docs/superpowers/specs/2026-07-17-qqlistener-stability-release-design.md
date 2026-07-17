# QQListener 稳定性、隐私与发布工程设计

## 目标

在保留现有 ClassIsland 插件功能、设置页、组件与自动化入口的前提下，解决审计中确认的隐私误收、读取失败后重复提醒、轮询并发与停止不完整、设置文件损坏、冷却时间失真、敏感日志、测试覆盖不足及发布不可复现问题。

本轮采用在现有分层上系统加固的方案，不重写整个插件为新的事件流框架。

## 已确认的产品决策

- 插件面向新版 NT QQ。
- `UserNotification.AppInfo` 不可用、应用身份为空或无法明确识别为 QQ/TIM 时，通知必须被忽略。
- 不再使用“任意双行 Toast 都可能是 QQ”的文本形状兜底。
- 隐私和错误恢复采用 fail-closed：无法确认来源、无法读取配置或监听已关闭时，不展示消息。
- 本轮同时完成运行时修复、自动化测试、版本同步、校验和验证、CI 和仓库清理。
- 不改写或强制移动已经存在的 `1.0.2.0` Git 标签；代码将阻止以后再次产生标签、manifest、DLL 和包版本不一致的发布。

## 范围

### 包含

- Windows 通知权限、读取结果与应用身份判断。
- 后台监听任务、手动轮询、取消与并发控制。
- provider 的通知状态、去重和冷却。
- 设置加载、原子保存、备份恢复及设置页保存生命周期。
- 最近消息、自动化重播、日志和诊断隐私。
- 单元/集成测试、样例验证、Windows CI、CIPX 和 MD5 校验。
- manifest、程序集版本和发布验证。
- README 与本地工具文件清理。

### 不包含

- UIA、QQ 进程注入或其他绕过 Windows 通知中心的采集方式。
- OCR、图片缩略图、独立弹窗样式或新的 TTS 功能。
- 改写 ClassIsland 宿主或移动已经发布的远端标签。
- 为无法确认来源的旧式 QQ Toast 恢复文本兜底。

## 运行时架构

运行链保持以下边界：

```text
WindowsNotificationReader
  -> WindowsNotificationReadResult
  -> QqNotificationPoller
       -> QqNotificationCandidateFilter
       -> QqToastMessageParser
       -> MessageEventPipeline / MessageRuleEvaluator
  -> QqNotificationProvider
       -> LatestMessageState
       -> ClassIslandMessageNotifier
```

新增 `QqNotificationPoller` 作为可独立测试的轮询协调器。它拥有通知 ID/指纹状态和完整轮询互斥；`QqNotificationProvider` 只负责 `IHostedService` 生命周期、启用状态和最终派发。这样可以在不构造 ClassIsland 通知提供器基类的情况下测试读取失败、并发轮询和状态提交。

## 通知读取与来源判断

`WindowsNotificationReader` 返回结构化的 `WindowsNotificationReadResult`，而不是用空数组同时表达“读取成功但没有通知”和“权限/API 失败”。结果至少区分：

- `Success`
- `PermissionDenied`
- `ListenerUnavailable`
- `ReadFailed`

只有 `Success` 可以提交新的通知快照。其他状态只更新诊断，不得清理 `_knownNotificationKeys`、active dedup key 或初始化基线。

权限请求只执行一次并缓存结果。拒绝状态不会在每个 0.3 秒轮询中重新请求；用户通过设置页“手动扫一次”可以显式重试。连续读取失败采用有上限的退避，成功后恢复用户配置的扫描间隔。

任何进入解析的通知都必须满足 `IsAppIdentityAvailable == true`。`QqOnly=true` 时还要求 `AppName` 或 `AppId` 明确匹配 QQ/TIM；`QqOnly=false` 时允许身份已知的非 QQ 通知，但仍拒绝未知身份。

QQ/TIM 身份匹配使用窄规则：显示名仅接受忽略大小写的 `QQ`、`腾讯QQ`、`TIM`；AppId 去除标点并转为小写后，只接受 `tencentqq`、`qqnt`、`comtencentqq`、`tencenttim` 或 `comtencenttim` 标记。单独出现 `Tencent`、`QQ` 或 `Tim` 不是充分条件。

应用身份未知时直接记录“无法确认应用来源，已忽略”，不调用解析器。原有 `LooksLikeQqToastShape` 不再参与 provider 的来源筛选，并删除把普通双行 Toast 固化为候选的测试期望。

## 轮询生命周期与并发

- `QqNotificationProvider` 保存 `_listenerTask`，启动后不再丢弃任务引用。
- `StopAsync` 先取消，再等待监听任务退出，最后释放 CTS 并更新诊断。
- `CancellationToken` 贯穿权限、读取、轮询等待和消息派发。
- `QqNotificationPoller` 使用 `SemaphoreSlim` 包住“读取 -> 计算 -> 状态提交”的完整过程，后台轮询与手动轮询不能交错提交新旧快照。
- 监听关闭或启用代次改变后，在途结果可以完成清理，但不得写入最近消息或展示提醒。
- 关闭监听时清除 `LatestMessageState`；自动化重播和设置页重播都要求监听仍启用。

## 去重与冷却

- 通知 ID + 内容指纹继续用于识别同一 Windows Toast 的内容变化。
- 内容 dedup key 继续用于可见通知和冷却去重，避免扩大本轮范围。
- `_seen` 的保留时间不得短于当前 `CooldownSeconds`；清理按每条记录实际过期时间执行。
- 读取失败不更新 active key；只有成功读取的快照能更新。
- 重置检测状态清空通知身份、可见 key 和冷却记录，并使下一次成功读取重新建立基线。

## 设置可靠性

设置保存采用同目录原子流程：

1. 序列化到临时文件；
2. flush 到磁盘；
3. 已有主文件时用 `File.Replace` 原子替换并生成 `.bak`；首次保存时使用原子 move；
4. 失败时保留原主文件并清理临时文件。

加载顺序：

1. 主文件有效：正常加载；
2. 主文件损坏但备份有效：加载备份、强制 `IsEnabled=false`、记录恢复告警，启动阶段不自动覆盖主文件；
3. 主文件和备份均无效：使用默认设置但强制 `IsEnabled=false`，保留坏文件并在诊断页显示错误。

设置页维护 dirty 状态。500ms 防抖仍用于常规输入，但页面 detach 前必须刷新未保存修改；订阅在 attached/detached 生命周期成对管理，保存异常进入诊断状态而不是抛向 UI。

监听实际使用的扫描间隔和时长在消费点再次夹紧，避免用户输入尚未经过防抖保存时让后台任务进入忙循环或异常退出。

## 隐私与诊断

- Information 日志不记录发送者、正文、黑名单内容、重要人物或关键词原文。
- 规则原因改为类别，例如“命中黑名单规则”“命中重要关键词”，不拼接配置值。
- 默认诊断只显示时间、消息级别和处理结果，不保存正文预览。
- Debug 日志同样不输出完整正文；需要定位解析问题时使用文本行数、长度、应用身份和不可逆内容指纹。
- `LatestMessageState.Clear()` 清除正文、发送者、来源、会话和状态标记。

## 测试设计

新增 `QQListener.Tests` 测试项目，并继续保留样例验证器。测试覆盖：

- AppInfo 不可用、空身份和非 QQ 双行 Toast 必须被拒绝。
- 明确 QQ/TIM 身份可以进入解析。
- `Success([])` 与读取失败对状态的影响不同。
- 并发调用只能串行读取和提交，旧快照不能覆盖新快照。
- 停止监听会等待任务结束，取消后不派发消息。
- 权限拒绝被锁存，不发生高频重复请求。
- 主设置损坏、备份恢复、双文件损坏和原子保存。
- 大于 600 秒的冷却仍按配置生效。
- 日志、诊断和规则原因不包含正文或私人规则值。
- 关闭监听清除最近消息，自动化不能重播。
- 原有 23 条样例按新隐私策略调整后继续验证解析和规则行为。

测试必须可使用 fake reader、fake clock 和派发回调运行，不依赖真实 Windows 通知权限。

## 构建、版本与发布

- 增加 `global.json` 固定 .NET 8 SDK，并启用 NuGet lock file。
- 在项目属性中显式设置 `Version`、`AssemblyVersion` 和 `FileVersion`，与当前 manifest 版本一致。
- 增加版本验证脚本，比较 manifest、项目版本、构建后的 DLL；标签构建时还比较 tag 名。
- 使用 Plugin SDK 自带的 `CreateCipx` 目标生成 CIPX 和 ClassIsland 要求的 MD5 `checksums.md`。
- 增加校验脚本，重新计算 CIPX MD5 并验证 `CLASSISLAND_PKG_MD5` 内容。
- Windows CI 顺序为：locked restore、Release build、测试、样例验证、版本检查、CIPX 打包、MD5 校验、上传构件。
- CI 在标签版本不一致时失败；已有 `1.0.2.0` 历史标签不在本轮自动移动。

## 仓库清理与文档

- 停止跟踪 `QQListener.sln.DotSettings.user` 和 `.stakpak/data/local.db`，并补充 `.gitignore`。
- 删除工作区中过时的根目录 MD5 临时文件，CIPX 目录仍作为构建产物忽略。
- README 增加隐私降级说明、构建、测试、打包、版本和兼容要求。
- 不修改与本轮无关的本地参考资料和用户问题数据。

## 验收标准

1. 身份未知的任意单行/双行/多行 Toast 均不会进入 QQ 解析或提醒。
2. 读取失败、权限拒绝或 listener 不可用不会清空成功读取建立的检测状态。
3. 后台和手动轮询无法并发提交，停止完成后没有遗留监听任务或消息派发。
4. 损坏设置不会被默认值静默覆盖，监听保持关闭且用户能看到诊断。
5. 1-3600 秒冷却范围全部按配置兑现。
6. 默认日志和诊断不包含发送者、正文或私人规则原文。
7. 关闭监听后最近消息清空，UI 与自动化均不能重播。
8. `dotnet build QQListener.sln --configuration Release --locked-mode` 成功且无错误。
9. `dotnet test QQListener.sln --configuration Release --no-build` 全部通过。
10. 样例验证器通过。
11. CIPX 中 manifest、DLL 版本与项目版本一致，生成的 MD5 摘要验证通过。
12. Windows CI 覆盖恢复、构建、测试、样例、打包和版本/校验检查。
