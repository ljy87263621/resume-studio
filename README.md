# 拾简 Resume Studio

<img src="client-winui/Assets/AppLogo.png" width="80" alt="拾简图标">

Windows 简历与求职管理软件，使用 WinUI 3 客户端和本地 Java 服务。

## 下载与安装

从 [GitHub Releases](https://github.com/scj040921/resume-studio/releases) 下载 `Setup-x64.exe`，点击“一键安装”，自动创建桌面和开始菜单快捷方式。也可使用完整的便携 ZIP。

需要 Windows 10 2004 或更新的 x64 系统。.NET 与 Java 随包提供；安装器会在缺少 WebView2 时联网补装。覆盖安装和卸载保留个人数据。

## 主要功能

- **简历工作台**：多份简历、模块编辑与拖动排序、顶栏排版设置、MiSans 与字体选择、照片、格式按钮、自动保存、撤销和历史版本。
- **模板与预览**：8 套开源岗位适配模板及 6 套兼容模板；有照片／无照片双预览。未填写处显示示例，填写后逐模块替换；PDF 只导出实际内容。
- **岗位优化**：本地职位对照；可配置兼容 AI 接口，逐项确认建议后生成独立岗位简历。
- **求职管理**：企业职位查询、链接／分享文本收藏、投递时间线、简历快照与复盘；投递和笔试／面试／截止日程相互关联与定位，保留筛选，重复收藏保护备注。
- **会话采集与导航**：BOSS／牛客在应用内正常浏览后，点击“读取当前页并收藏”直接保存；自动采集默认关闭，开启后连续浏览也会直接收藏。重复职位保护原有内容，失败可重试。窗口最多展示 500 条，清空列表不删除收藏；登录和验证需本人完成。
- **手动同步**：设置页可配置 WebDAV，整包同步简历、照片、历史、收藏、投递、日程、公司来源和非敏感设置。双方修改时选择保留方向，下载替换前备份本机资料；要求强 ETag 和条件 PUT。[同步协议](docs/SYNC-PROTOCOL.md)说明格式和恢复方式。
- **界面与更新**：经典／Haru 界面、主题与动效设置，Stable／Nightly 更新和公益代理备用通道。

数据保存在 `%LOCALAPPDATA%\ResumeStudio`。WebDAV 同步包在用户选择的服务器上以明文保存；Cookie、API Key 和同步密码仅留本机。当前没有 Word 导出；职位覆盖和访问状态以官网为准，不同步平台账号收藏。AI 仅在主动请求时发送内容，Key 按当前 Windows 用户加密保存。日程提醒仅在软件运行时检查。真实平台登录、线上 WebDAV 和干净 Windows 分发仍需验收，详见[集成说明](docs/TAKEOVER.md)。

## 开发

需要 Windows、PowerShell 7、.NET 8 SDK 和 JDK 21，设置 `JAVA_HOME` 后运行：

```powershell
./scripts/build.ps1
./scripts/test-offline.ps1
./scripts/test-workbench.ps1
./scripts/test-features.ps1 -ReuseBackendTestClasses
```

完整程序位于 `release/ResumeStudio/`。构建产物、验证输出、个人数据和私钥不提交到仓库；生成的模板目录由构建脚本重建。

## 文档

- [安装与卸载](docs/INSTALLATION.md) · [更新与发布](docs/UPDATES.md)
- [开发、结构与验证](docs/DEVELOPMENT.md) · [版本记录](docs/CHANGELOG.md) · [图标](docs/BRAND.md)
- [模板与字体许可](THIRD_PARTY_NOTICES.md) · [运行组件许可](RUNTIME_NOTICES.md)

原创代码采用 [MIT](LICENSE)，第三方模板、字体和运行组件遵循各自许可。
