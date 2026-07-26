# QQListener 设置原子替换测试跨平台设计

## 背景

`QqListenerSettingsTests.FailedReplacePreservesFilesAndDeletesTemporaryFile` 通过保持 `settings.json` 的只读句柄来制造 `File.Replace` 失败。该方法在 Windows 上会触发缺少 `FileShare.Delete` 的共享冲突，但在 Unix 上不会阻止基于 `rename` 的原子替换，因此 macOS fresh 全量测试得到“未抛出异常”。

这是测试故障注入不具备跨平台真实性，不是 macOS 产品保存缺陷。生产逻辑仍需要同时证明：正常替换遵循各平台文件系统语义；真正的替换失败会传播异常、保留主文件和备份，并删除已写入的临时文件。

## 目标与约束

- 不改变 `QqListenerSettings.Save`、`ModifyAndSave`、备份轮转或 JSON 格式的公开行为。
- Windows 继续验证打开且未共享删除的主文件会阻止替换。
- Unix 明确验证打开读句柄不会阻止原子替换，主文件和备份按正常顺序推进。
- 所有平台确定性验证替换阶段抛出 `IOException` 后主文件、备份和临时文件的状态。
- 测试故障注入必须是实例级，避免静态 hook 污染并行测试。
- 测试程序集仅通过现有 `InternalsVisibleTo` 使用该入口；不扩大公开 API。

## 方案

### 实例级替换边界

`QqListenerSettings` 增加一个只读实例委托，签名与当前四参数 `File.Replace` 一致。公开无参构造函数把它绑定到 `File.Replace`；internal 构造函数允许测试传入替换实现。`SaveCoreAtomic` 的两个替换分支都调用该委托，参数、分支顺序和 `ignoreMetadataErrors: true` 保持不变；首次保存的 `File.Move` 分支不变。

该边界只隔离操作系统相关的替换调用，不伪造目录创建、临时文件写入、序列化、flush、备份状态或 `finally` 清理。

### 平台语义测试

现有读句柄测试改为一个非空断言的跨平台行为测试：

- Windows：`Save` 抛出 `IOException`，主文件与备份内容不变，无临时文件残留。
- 非 Windows：`Save` 成功，主文件更新为第三版，备份推进为第二版，无临时文件残留。

这样每个平台都验证真实行为，不使用“非目标平台直接 return”的伪通过。

### 确定性失败测试

先用默认实例创建第二版主文件和第一版备份，再用指向同一目录、注入替换委托的新实例保存第三版。委托记录源临时路径并抛出 `IOException`。测试断言：

- 替换边界被调用时临时文件已经存在；
- 原始 `IOException` 向调用方传播；
- 主文件仍为第二版，备份仍为第一版；
- 记录的临时文件以及 `settings.json.*.tmp` 均已被 `finally` 删除。

## 测试真实性

先加入使用 internal 构造函数的确定性失败测试并运行，确认因入口不存在而 RED；随后实现最小委托边界并转为 GREEN。再临时移除或绕过 `finally` 的临时文件删除，确认确定性失败测试因残留临时文件变红，立即还原并重跑为绿。

定向验证运行整个 `QqListenerSettingsTests`。最终 fresh 门禁仍使用用户指定的未过滤 `dotnet test QQListener.sln`；Windows GitHub CI 负责同时执行真实共享冲突分支。

## 非目标

- 不引入完整文件系统抽象、静态全局 hook 或额外第三方依赖。
- 不改变原子保存、异常传播、设置安全默认值或任何 QQ/TIM 身份规则。
- 不借此重构设置模型或持久化协调器。
