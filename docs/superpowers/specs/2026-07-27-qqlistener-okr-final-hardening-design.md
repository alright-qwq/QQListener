# QQListener 全量 OKR 最终加固设计

## 目标

在不重新实现既有 Task 1–8 的前提下，修复 `codex/okr-completion` 最终复审发现的发布阻断项，收紧宿主冒烟证据边界，并补齐隐私与自动化关键回归测试。完成后执行全量发布门禁，非 squash 合并到 `master`。

## 产品约束

- 默认面向新版 NT QQ。
- 未知或空应用身份始终拒绝，不因关闭 `QqOnly` 而放宽。
- TIM 仅可能兼容，不承诺应用身份兼容。
- 保持版本 `1.1.0.0`、现有 Trigger/Action ID 和中文提交历史。
- 不移动历史标签，不重写或 squash 现有提交。

## 复审结论

全分支复审未发现 Critical。合并前必须修复的 Important 是 CIPX verifier 缺少通用资源上限：当前只限制 `manifest.yml` 和 `QQListener.dll`，其余条目没有单项、条目数或累计解压量限制，并使用 `File.ReadAllBytes` 计算整包 MD5。

宿主冒烟记录还需要收紧证据范围：沙箱可能同时包含根 `host\Plugins\QQListener` 和 `host\data\Plugins\QQListener`，现有记录没有保存实际加载路径、DLL 哈希或原始脱敏日志，因此不能把启动日志唯一归因到 `data` 副本。

## CIPX 有界流式验证

沿用现有路径安全、大小写重复、保留文件名和文件/目录前缀冲突算法，不改其语义。增加以下统一资源预算：

- CIPX 文件最大 `256 MiB`。
- `checksums.md` 最大 `1 MiB`。
- ZIP 条目最多 `512` 个。
- 每个普通条目最大 `128 MiB`。
- 所有条目累计实际解压量最大 `256 MiB`。
- `manifest.yml` 继续使用更严格的 `1 MiB` 上限。
- `QQListener.dll` 继续使用 `128 MiB` 上限。

校验顺序调整为：

1. 检查 CIPX 和 `checksums.md` 的存在性及文件大小。
2. 使用 `FileStream` 流式计算 MD5，并在 checksum 失败时停止，不创建解压目录。
3. 打开 ZIP，先审计路径、冲突、条目数、单项声明长度和累计声明长度。
4. 只有预扫描通过才创建随机临时目录。
5. 实际复制时再次检查单项字节数和累计字节数，防止元数据与实际流不一致。
6. 无论成功或失败都按现有规则清理临时目录。

资源超限只报告固定错误类型和条目路径，不读取或回显条目内容。

## 宿主冒烟文档修正

不伪造或补写不存在的 Windows 冒烟证据。验证文档改为明确说明：

- 首次继承旧状态的冒烟继续保持作废。
- 最终运行使用了全新的 `host\data` 状态根。
- 复制整个宿主后根 `Plugins` 副本是否仍存在、实际加载哪一份插件，本次记录无法独立证明。
- “QQListener 监听已启动”日志只证明 Provider、DI 和 HostedService 启动；设置缺失时实际轮询保持关闭。
- Markdown 是操作者验证记录，未提交原始日志、完整 transcript、加载路径或 DLL 哈希。
- 正常退出仍无法安全自动化，强制结束只用于已取证沙箱清理。

因此结论限定为“无既有状态的沙箱宿主启动，且某个已同步的 QQListener 1.1.0.0 副本完成 Provider 注册”；不再宣称唯一验证了 `host\data` 插件副本或真实通知链。

## 隐私与自动化回归

- 隐私模式下的“QQ 普通/重要/呼叫消息”是脱敏占位，不属于消息正文；即使 `ShowMessageBody=false` 也应保留该占位，避免组件可见但完全空白。
- 新增直接测试，证明 Replay 不发布 `MessageAutomationEventHub` 事件。
- 新增日志测试，证明自动化订阅者异常日志不包含发送者、会话、正文或异常消息，只包含订阅者类型和异常类型。

## 测试驱动与验证

实现必须先增加失败测试并确认 RED，再写最小生产代码使其 GREEN。至少覆盖：

- checksum 失败不会创建解压目录。
- 超过条目数上限在解压前拒绝。
- 普通单项和累计解压预算的边界逻辑。
- MD5 改为流式后仍能接受正确 marker、拒绝错误 marker。
- 隐私模式与正文关闭组合仍显示脱敏占位。
- Replay 不发布 Hub；Hub Error 日志不包含私密内容或异常消息。
- 修正文档的证据边界和受限结论。

最终执行 locked restore、Release build、全量 xUnit、23 个 Toast 样例、CIPX 重建、完整 Release verifier 和 `git diff --check`。若当前非 Windows 环境需要交叉构建，只允许通过进程级 `EnableWindowsTargeting=true`，不得修改项目目标框架或产品配置。

## 合并与发布

所有 Critical/Important 清零、修复复审通过且 fresh 门禁成功后：

1. 保留提交历史，非 squash 合并 `codex/okr-completion` 到 `master`。
2. 确认本地 `master` 指向最终验证提交。
3. 仅在单次 `git push origin master` 进程中清空代理环境变量，不修改 Git 或系统代理配置。
4. 使用 `git ls-remote origin refs/heads/master` 核对远端哈希。
5. 在 `master` 推送成功前保留功能分支。
