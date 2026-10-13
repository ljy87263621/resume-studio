# Windows 安装与卸载

下载与通道对应的 Setup-x64.exe，双击后在中文首页点击“一键安装”。默认安装到当前用户的 `%LOCALAPPDATA%\Programs\ResumeStudio`，自动创建桌面“拾简 - 简历制作与投递”和开始菜单快捷方式，完成页可直接启动。

安装器以当前用户权限运行。使用中的程序文件由 Windows Restart Manager 检测，安装时可选择正常关闭正在运行的拾简；不会强制终止所有同名进程。建议先保存并关闭正在编辑的窗口。

应用自带 .NET、Windows App SDK 和精简 Java 运行时。缺少 WebView2 时，安装器调用随包的微软官方 Evergreen Bootstrapper，联网完成安装。已安装运行时的机器无需重复下载。WebView2 共享运行时不会随拾简卸载。

## 数据和覆盖安装

Stable 和 Nightly 使用同一个应用标识与安装目录。应用数据保存在 `%LOCALAPPDATA%\ResumeStudio`，与程序目录分开。重复安装和切换通道不会清空简历、收藏、投递、日程、历史版本或 AI 设置。

Windows“设置 → 应用 → 已安装的应用”中的拾简条目提供卸载入口。卸载移除安装器登记的程序文件及快捷方式，保留独立的个人数据目录和安装目录中未登记的额外文件。没有递归删除整个应用或数据目录的规则。

软件内更新仍使用签名 ZIP 和原有事务替换机制；卸载器、桌面快捷方式及开始菜单快捷方式保留。便携 ZIP 仍可使用，但它不会自行登记卸载入口或创建快捷方式。

## 构建与验证

CI 获取固定 Inno Setup 6.7.3，核对 SHA-256 及厂商 Authenticode 签名；微软 WebView2 Bootstrapper 也验证发布者签名。编译器仅在构建环境使用，安装包内附带其许可。

build-installer.ps1 检查与 ZIP 相同的应用文件清单，并编译完整 Setup。test-installer.ps1 仅允许在一次性的 GitHub Windows runner 中执行，验证安装、所有文件哈希、两种快捷方式的目标／图标／工作目录、卸载注册、重复安装、Java 运行及卸载后个人数据保留。它不替换开发者本机的实际安装。

发布页同时保留 Setup、ZIP、签名元数据和 SHA256SUMS。签名元数据额外记录 Setup 文件名、大小和 SHA-256；软件更新通道继续使用 ZIP 字段，兼容旧更新器。
