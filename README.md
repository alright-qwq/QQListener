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

## 说明

- 原作者是B站@BSOD-MEMZ，github原仓库：https://github.com/BSOD-MEMZ/QQListener
- 当前版本先移植了 Python 版的 WinSDK 通知监听主链路。图片缩略图、独立弹窗样式、TTS 细项和 UIA 备用模式还没有迁移。PS:未来可能也不会做(bushi
- 如遇到问题，欢迎查看Q&A：https://al-right.top/archives/58 或是在本项目提出issue！
