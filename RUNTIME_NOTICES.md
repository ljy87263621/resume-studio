# 随应用分发的运行环境

拾简发布包自包含 .NET 8、Windows App SDK 以及精简 OpenJDK 21 运行时。这些组件适用各自的许可，不受项目根目录 MIT 许可证重新授权。

## OpenJDK / Microsoft Build of OpenJDK

v0.3.0 至 v0.7.1 及当前 Nightly Windows x64 发布包的 Java 运行时由 Microsoft Build of OpenJDK **21.0.12.1** 使用 `jlink` 生成，仅包含 `java.base`、`java.net.http`、`jdk.httpserver`、`jdk.crypto.ec` 及其依赖。保留 `runtime/legal/` 内的 GPLv2、Classpath Exception、Assembly Exception 和各模块第三方说明。

- 官方二进制：[Microsoft OpenJDK 下载](https://learn.microsoft.com/en-us/java/openjdk/download)。对应源码：[microsoft/openjdk-jdk21u](https://github.com/microsoft/openjdk-jdk21u/tree/6f26c6b2b098814858e76ad2986c01d5e1c8987a)，提交 `6f26c6b2b098814858e76ad2986c01d5e1c8987a`（`release/jdk-21.0.12.1_1`）。
- 对应源码归档 `microsoft-openjdk-jdk21u-6f26c6b2.tar.gz` 同时提供于[拾简 v0.3.0 Release](https://github.com/scj040921/resume-studio/releases/tag/v0.3.0)，文件保持上游原样并提供 SHA-256 校验。
- 运行时仅经过模块裁剪、调试信息及文档去除，未修改 OpenJDK 源码。构建方法见 `scripts/build.ps1`。Java / OpenJDK / Microsoft Build of OpenJDK 商标归各自权利人。

其他自行构建的版本应同步提供所用 JDK 的许可证和对应源码。

## Microsoft 运行组件

- [.NET 运行时](https://github.com/dotnet/runtime)：MIT 及相应第三方许可。随发布包保留 `licenses/Microsoft.NETCore.App.Runtime.win-x64-<版本>/` 中的许可证和第三方说明。
- [Windows App SDK](https://github.com/microsoft/WindowsAppSDK)：随包保留所用官方 NuGet 组件的许可证和第三方说明，见 `licenses/` 下 WinUI、Foundation、Base、InteractiveExperiences 等子目录。v0.4.2 按需引用 WinUI 组件，未使用的 AI / ML / Widgets 不参与分发。
- WebView2 托管加载组件随客户端依赖分发，适用 Microsoft 相应组件许可，见 `licenses/Microsoft.Web.WebView2-<版本>/`。WebView2 浏览器运行时需系统已安装，不包含在本发布压缩包中。

准确条款以随附的完整许可证文本为准。
