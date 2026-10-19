# 安装与卸载

1. 从 [GitHub Releases](https://github.com/ljy87263621/resume-studio/releases) 下载对应通道的 `Setup-x64.exe`。
2. 双击后点击“一键安装”，自动创建桌面和开始菜单快捷方式。
3. 完成页可直接启动拾简。

默认安装到 `%LOCALAPPDATA%\Programs\ResumeStudio`，使用当前用户权限。缺少 WebView2 时通过微软官方引导程序联网补装；.NET 和 Java 随应用提供。覆盖安装前先保存并关闭软件；安装器也会检测使用中的文件并提示正常关闭。

Stable／Nightly 共用安装目录。个人数据位于 `%LOCALAPPDATA%\ResumeStudio`，覆盖安装和卸载均保留。Windows“设置 → 应用”可卸载；安装器只删除其登记的程序文件和快捷方式，保留未登记的额外文件，不移除共享 WebView2。

软件内更新继续使用签名 ZIP，保留卸载器与快捷方式。便携 ZIP 可直接解压运行，但不自动创建快捷方式或卸载入口。

构建工具来源与许可见 [运行组件](../RUNTIME_NOTICES.md) 和 [第三方说明](../THIRD_PARTY_NOTICES.md)；发布提供 SHA-256，Setup 摘要也纳入签名元数据。

程序及最近一份回退备份使用 Windows LZX 压缩，仅处理清单内未修改的程序文件，保持内容和签名不变。不支持压缩的卷或使用中的文件不会阻止安装。设置中的“本机存储”显示实际磁盘占用、更新缓存和旧程序备份，并提供存储优化入口。
