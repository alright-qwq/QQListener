# QQListener

QQListener 是 ClassIsland 插件：读取 Windows 通知中心中的 QQ 通知，并按你的规则显示提醒。默认支持新版 **NT QQ**。

## 五步快速开始

1. 安装并登录新版 NT QQ，在 QQ 中开启需要的消息通知。
2. 打开 Windows“设置 → 系统 → 通知”，允许 QQ 发送通知；不要只依赖 QQ 窗口内的提示。
3. 在 ClassIsland 安装 QQListener，首次使用时允许它访问通知中心。
4. 打开 QQListener 设置，确认“监听”已开启，并按需设置关键词、重要联系人和冷却时间。
5. 向 QQ 发送一条测试消息，在诊断页确认已读取并按规则处理。

TIM 只有在通知**格式兼容**时才可能被解析；不承诺 TIM 的应用身份与新版 NT QQ 兼容。应用身份为空或未知时，QQListener **始终拒绝**该通知，不能通过关闭“仅处理 QQ 通知”来放宽这一安全限制。

## 自动化、组件与规则

QQListener 提供以下自动化 Trigger ID，供你在 ClassIsland 工作流中手动选择使用；插件不会自动创建工作流。

- `QQListener.NormalMessageReceived`：收到普通 QQ 消息。
- `QQListener.ImportantMessageReceived`：收到重要 QQ 消息。
- `QQListener.CallingMessageReceived`：收到 QQ 呼叫。

可在工作流中使用的 Action ID：

- `QQListener.SetListenerEnabled`：设置监听开关。
- `QQListener.ShowLatestMessage`：重新显示最后一条 QQ 消息。
- `QQListener.ClearLatestMessage`：清空最后一条 QQ 消息。

“最后 QQ 消息”组件可设置最低消息等级（普通、重要、呼叫）、是否显示正文，以及隐私模式。隐私模式只显示 QQ 来源和消息等级，不显示发送人、群聊或正文。

规则预设可作为起点，再按你的需要调整：

- 作业提醒：关键词为“作业、提交、截止、收作业”。
- 老师呼叫：联系人为“老师、班主任”，并启用“呼叫”。
- 考试通知：关键词为“考试、监考、考场、通知”。

规则预览和诊断都只在本机运行：不联网，也不会上传消息内容。

## 故障排查

- **通知权限恢复**：若诊断页显示权限未授予或不可用，先在 Windows 通知设置中重新允许 QQ 通知，再重启 QQ 和 ClassIsland；恢复后用一条新通知验证。
- **没有消息**：确认使用新版 NT QQ、Windows 通知中心里确实出现 QQ 通知、监听已开启，并查看诊断页的最近读取与忽略原因。
- **重复消息**：检查 QQ 是否同时产生多条相同 Toast；QQListener 会按通知内容去重，仍有重复时可适当增加冷却时间，并把诊断信息附到反馈中。
- **未知格式**：诊断显示“未知格式，已降级解析”时，插件会尽量保留可读内容；请保留该诊断结果并提供不含隐私的通知结构说明，便于后续兼容。
- **身份不可用**：诊断显示“未知应用身份”或 AppInfo 不可用时，该通知会被拒绝。这是隐私保护设计；请恢复 Windows/QQ 通知权限或更新 QQ，不要尝试放宽身份检查。

## 开发与发布

历史标签 `1.0.2.0` 指向清单版本为 `1.0.1.0` 的历史提交，保留不动。发布版本以 `Directory.Build.props` 的 `QQListenerVersion` 和 `manifest.yml` 的四段式版本为准。
