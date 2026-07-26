# QQListener 全量 OKR 完成设计

## 目标与基线

本设计补齐 `QQListener-OKR.local.md` 中尚未完成或只有部分证据的 O1～O4，同时保留已稳定的 NT QQ 通知读取、严格应用身份过滤、并发取消、原子设置持久化、隐私日志和发布门禁。

当前 `1.0.3.0` 已经发布并存在同名标签。本次包含规则、自动化和组件新增能力，完成版使用 `1.1.0.0`；不得移动既有标签。

实现采用渐进扩展，不引入通用可排序规则 DSL，也不改变 `MessageEvent` 的公开字段。

## 总体架构

运行链路保持：

```text
WindowsNotificationReader
  -> QqToastMessageParser
  -> MessageRuleEvaluator
  -> MessageEventPipeline
  -> QqListenerWorker
       -> MessageAutomationEventHub -> ClassIsland Trigger
       -> LatestMessageState
       -> ClassIslandMessageNotifier
```

新增能力分为四个边界：

1. `MessageRuleEvaluator` 继续是唯一规则判定入口；来源和时间段也在这里判定。
2. `MessageRulePreviewService` 复用正式解析器与规则评估器，为设置页提供无副作用预览。
3. `MessageAutomationEventHub` 只发布真实轮询中已接受的事件；重播不会再次触发自动化。
4. 最新消息组件只读取 `LatestMessageState`，展示过滤和脱敏只影响组件，不改变通知和自动化。

## O1：格式变化的可见诊断

`QqToastMessageParser` 为解析结果增加置信度信息：

- 已知格式：单行“发送者：正文”、群聊多行等现有格式，原因保持“已解析 QQ Toast”。
- 降级格式：非空但只能按正文或两行兜底解析，仍生成消息事件，但原因明确为“未知格式，已降级解析”。
- 空格式：继续解析失败并记录“通知没有可解析文本”。

轮询器把降级原因写入诊断状态和结构化日志，但不得记录发送者或正文。降级事件仍进入规则链，避免格式轻微变化时完全丢消息。

## O2：规则、展示行为与规则测试

### 设置模型

在 `QqListenerSettings` 增加：

- `AllowedSourcesText`：每行一个来源 ID；空值表示允许全部。当前正式来源为 `qq.toast`。
- `RuleTimeWindowEnabled`：是否启用允许时间段。
- `RuleTimeWindowStartText` / `RuleTimeWindowEndText`：`HH:mm`，按消息本地时间判断。
- `NormalTopmost` / `ImportantTopmost` / `CallingTopmost`：三种可展示等级独立控制置顶，默认分别为 `false`、`false`、`true`，保持已有行为。

忽略等级不会生成展示请求，因此没有时长或置顶设置。普通、重要、呼叫继续分别使用现有三个时长字段。

时间段采用“起点包含、终点不包含”：

- `08:00`～`18:00` 接受白天消息。
- `22:00`～`07:00` 自动按跨午夜处理。
- 起止相同表示全天，避免误配置后屏蔽所有消息。
- 无法解析的时间由 `Normalize` 恢复为 `00:00` / `00:00`，即全天。

### 规则优先级

固定优先级为：

1. 黑名单 → 忽略。
2. 来源不在允许列表 → 忽略。
3. 不在允许时间段 → 忽略。
4. 呼叫关键词 → 呼叫。
5. 重要人物 → 重要。
6. 重要关键词 → 重要。
7. `有人@我` → 重要。
8. 其他 → 普通。

诊断原因只描述规则类型，不回显用户配置值或消息内容。

### 规则测试入口

新增 `MessageRulePreviewService`，输入模拟 Toast 文本、来源 ID 和可选 `HH:mm` 时间：

- 使用 `QqToastMessageParser` 解析多行文本。
- 以临时时间和来源副本调用 `MessageRuleEvaluator`。
- 返回是否解析成功、等级、是否忽略和解释原因。
- 不写入去重状态、最近消息、诊断或自动化事件。

设置页“过滤规则”区增加折叠的“规则测试”项，包含文本输入、来源、时间、测试按钮和只读结果。默认来源 `qq.toast`、时间为当前本地时间。

### 官方预设

新增纯代码 `QqListenerRulePresets`，提供三个幂等预设：

- 作业提醒：重要关键词为作业、提交、截止、收作业。
- 老师呼叫：启用呼叫，关键词为呼叫；重要人物为老师、班主任。
- 考试通知：重要关键词为考试、监考、考场、通知。

应用预设会替换规则相关字段，不修改监听开关、扫描间隔、去重、来源或时间段。设置页明确提示“将替换人物/关键词/呼叫规则”，按钮点击后通过 `ModifyAndSave` 原子保存。

## O3：ClassIsland 自动化与组件

### 消息触发器

新增单例 `MessageAutomationEventHub`，在 `QqListenerWorker` 更新 `LatestMessageState` 后、显示通知前发布事件。只发布正式轮询接受的事件，忽略事件和重播事件不发布。

注册三个无设置触发器：

- `QQListener.NormalMessageReceived`
- `QQListener.ImportantMessageReceived`
- `QQListener.CallingMessageReceived`

每个 Trigger 在 `Loaded` 时订阅 Hub，在 `UnLoaded` 时取消订阅，仅匹配自己的 `MessageImportance` 后调用 `Trigger()`。同一事件只触发一个等级触发器。

### 第三个行动

新增 `QQListener.ClearLatestMessage` 行动，调用 `LatestMessageState.Clear()`。与现有“设置监听状态”“显示最后一条消息”共同组成三个可组合行动，并加入 QQListener 行动菜单。

### 最新消息组件

组件设置增加：

- `MinimumImportance`：`Normal`、`Important` 或 `Calling`。
- `ShowMessageBody`：是否显示正文，默认 `true`。
- `HideSensitiveContent`：隐私模式；开启时隐藏发送者、会话和正文，只显示来源及等级占位。

现有 `ShowSender` 和 `ShowStatusTag` 保留。若最新消息低于最低等级，组件按现有 `ShowPlaceholder` 决定显示占位还是隐藏。`Calling` 过滤自然满足“只显示呼叫”，`Important` 同时显示重要和呼叫。

组件展示决策提取为可单元测试的纯方法或 presenter，避免依赖 Avalonia 视觉树才能验证。

## O4：文档、发布内容与干净安装

README 增加：

- 五步快速开始。
- Windows 通知权限恢复和常见故障排查。
- 明确 NT QQ 为默认支持范围；TIM 仅在通知格式兼容时可能解析，不承诺应用身份兼容，未知身份仍拒绝。
- 隐私边界、诊断信息和规则测试不发送数据到网络。
- 三个自动化触发器、三个行动、组件过滤和预设的使用说明。

新增根目录 `CHANGELOG.md`，记录 `1.1.0.0` 变更，并将其复制进构建输出和 CIPX。

发布校验器增加“干净安装冒烟”：

1. 创建全新临时目录。
2. 安全解压 CIPX，拒绝路径穿越和重复项。
3. 要求根目录包含 `manifest.yml`、`QQListener.dll`、`README.md`、`CHANGELOG.md` 和 `icon.png`。
4. 从解压目录读取 manifest 和 DLL，验证版本均为 `1.1.0.0`。
5. 清理临时目录，并由测试证明成功和失败路径都不会遗留文件。

本机存在相邻 ClassIsland 源码。自动门禁通过后执行 Debug 构建同步插件，再启动一次宿主或其可用烟雾入口；若宿主需要交互，记录手动检查项和无法自动化的原因，不以伪造证据代替真实验证。

## 兼容性与迁移

- 新设置字段均有保持当前行为的默认值，旧 `settings.json` 可直接反序列化。
- `Normalize` 只修正无效时间和范围，不覆盖合法用户设置。
- 预设必须由用户主动点击，不在升级时自动应用。
- 自动化 Trigger/Action ID 发布后视为稳定 API，不随显示文案改变。
- 现有通知提供方、组件 GUID、两个行动 ID 和历史标签保持不变。

## 错误处理与隐私

- 规则预览解析失败只更新预览结果，不写全局“最近错误”。
- 自动化 Trigger 的订阅必须可重复 Loaded/UnLoaded 且不重复触发。
- Hub 的单个订阅者异常不得阻止通知展示；异常只记录类型化日志，不含消息正文。
- 组件隐私模式不得把敏感内容放进隐藏控件文本或辅助状态。
- 日志和诊断继续禁止发送者、正文、私有规则值。

## 测试与验收

新增或扩展测试覆盖：

- 已知/降级/空 Toast 解析原因。
- 来源规则、普通和跨午夜时间段、边界、无效时间、规则优先级和脱敏原因。
- 普通/重要/呼叫三种置顶组合与三个展示时长。
- 规则预览无副作用。
- 三个 Trigger 的加载、卸载、单次匹配和忽略/重播不触发。
- 清空最后消息行动和三行动注册。
- 组件最低等级、占位、隐藏发送者/正文、隐私模式组合。
- 三个预设内容、幂等性和不修改非规则字段。
- README/CHANGELOG 打包、空目录解包安装、临时目录清理。

最终门禁顺序：锁定还原、Release 构建、全量 xUnit、样例验证、元数据预检、CIPX 重建、完整发布/干净安装校验、ZIP/MD5 审计、Debug 宿主同步与可行的宿主烟雾、Git 差异检查。

## 非目标

- 不实现任意数量、拖拽排序的通用规则 DSL。
- 不接入新的消息来源或放宽未知应用身份过滤。
- 不把规则测试结果写入真实最近消息或自动化。
- 不自动创建或覆盖用户的 ClassIsland 工作流。
- 不移动 `1.0.2.0`、`1.0.3.0` 等历史标签。
