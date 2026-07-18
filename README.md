# QQListener

QQListener 是一个由 PySide6 平台移植而来的 ClassIsland 插件，用于监听 Windows 通知中心中的 QQ 通知，并把新消息转发为 ClassIsland 提醒。

## 当前功能

- 通过 Windows 通知中心读取 Toast 通知。
- 可只处理 QQ 通知。
- 支持重要人物、重要关键词、黑名单过滤。
- 支持“呼叫”关键词，呼叫消息会使用更长显示时间并置顶提醒。
- 在 ClassIsland 设置页中提供基础配置。

## 使用前准备

1. 使用新版 NT QQ。
2. 在 Windows 中允许 QQ 发送通知。
3. 首次运行时允许 QQListener 访问通知中心。
4. 在 ClassIsland 的 QQListener 设置页中按需配置关键词和时长。

## 通知来源与隐私

- 本项目默认面向新版 NT QQ，通过 Windows 通知中心提供的应用身份识别 QQ 通知。
- 如果 Windows 无法提供通知的应用身份，QQListener 会有意忽略该通知，不会仅凭通知文本猜测来源；具体原因可在插件诊断页面查看。
- 开启“仅处理 QQ 通知”时，只接受身份明确属于 QQ 的通知。关闭该选项时，也只会放行身份明确的非 QQ 通知，身份为空或未知的通知仍会被拒绝。
- 关闭监听会立即丢弃在途轮询结果，并清空缓存的最后一条消息，防止之后通过重播功能再次显示旧的私人内容。

## 开发与发布

在仓库根目录依次执行以下命令，可完成锁定依赖还原、Release 构建、测试、样例校验、CIPX 打包和发布产物校验：

```powershell
dotnet restore QQListener.sln --locked-mode
dotnet build QQListener.sln --configuration Release --no-restore
dotnet test QQListener.sln --configuration Release --no-build
dotnet run --project Samples/QQListener.SampleVerifier/QQListener.SampleVerifier.csproj --configuration Release --no-build
dotnet build QQListener.csproj --configuration Release --no-restore -p:CreateCipx=true
dotnet run --project Tools/QQListener.ReleaseVerifier/QQListener.ReleaseVerifier.csproj --configuration Release --no-build -- --root . --configuration Release
```

现有标签 `1.0.2.0` 指向一个清单版本为 `1.0.1.0` 的历史提交，不得强制移动该标签。后续发布必须先同时递增 `manifest.yml` 中的版本和 `QQListenerVersion`，再创建新的四段式版本标签。

## 说明

- 原作者是B站@BSOD-MEMZ，github原仓库：https://github.com/BSOD-MEMZ/QQListener
- 当前版本先移植了 Python 版的 WinSDK 通知监听主链路。图片缩略图、独立弹窗样式、TTS 细项和 UIA 备用模式还没有迁移。PS:未来可能也不会做(bushi
- 如遇到问题，欢迎查看Q&A：https://al-right.top/archives/58 或是在本项目提出issue！
