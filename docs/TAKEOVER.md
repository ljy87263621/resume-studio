# 本机 fork 接管说明

本轮基于上游 [scj040921/resume-studio](https://github.com/scj040921/resume-studio)，接管基准为提交 `e71aa3d`。维护目标仓库为 [ljy87263621/resume-studio](https://github.com/ljy87263621/resume-studio)。本机功能改动与上游发行记录需要分别核对；本文说明当前实现和待验收范围，不代表已发布新版。README 与历史迭代说明中的“单机无同步”“BOSS/牛客未接列表”“启动默认检查”等旧描述属于上游历史，当前改动以本文及 [同步协议](SYNC-PROTOCOL.md) 为准。

## 技术栈与可行路线

延续 C#/.NET 8 + WinUI 3 原生客户端、WebView2 浏览/HTML 预览/PDF 导出与 Java 21 loopback 后端。后端不依赖第三方 Java 库，以原始 JSON 文件保存简历、收藏、投递、来源和日程。现有目录分发路线可继续复用 .NET/Windows App SDK 自包含输出与 jlink 精简 Java 运行时，无需要求用户安装 Java/.NET，也无需 MSIX/AppX 注册。WebView2 是实际系统依赖：Portable 需要机器具备 Runtime，传统 Setup 在缺失时补装；完全离线、原生 Windows 空白环境需另行提供离线 Runtime 安装资源或评估固定版本随包体积及许可。

当前阶段沿用现有架构完成本机可审阅迭代，使用离线功能测试、客户端构建、UI 验收与隔离安装测试收敛风险。扩展同步通过 provider 接口追加，招聘采集通过用户可见 WebView2 浏览会话追加；没有另建远程账号服务器、平台登录后端或新数据库。协议 schemaVersion 与应用版本独立。完整 Portable 构建已成功，0 warning / 0 error；中文路径导致的 mt.exe 构建问题通过 `D:\Develope\Build\codex-resume-studio` Junction 别名指向原仓库完成重建，源码仍在同一目录。41 项原生双界面导航检查已通过。

## 当前功能边界

- 设置页提供 WebDAV 配置及“检查并同步”，完整同步五类实体、内嵌照片、压缩历史解包资料与白名单设置。冲突明确选择整包方向，传播删除；导入前备份及恢复 API 见协议。GitHub 仅预留 ISyncRemoteStore 契约。
- BOSS 直聘、牛客入口打开应用内可见浏览器；用户正常浏览、登录或完成验证后，自动采集该会话可读取的职位 JSON、页面初始数据和可见 DOM。列表先展示“待核对”职位，用户选择并确认后批量保存收藏。保留原职位链接，去重使用规范化链接，已有收藏不重复创建。当前每次最多采集 500 条、确认保存最多 50 条；不等于全站完整列表。
- 采集只提取职位相关字段，不导出 Cookie、账号登录信息、聊天响应或验证内容；平台会话留在本机 WebView2。站点结构、响应路径或反爬策略变化可能导致读不到或仅有摘要；需用户核对，详情可沿用“浏览并读取”补全。没有后台无人值守全站抓取、验证码绕过或自动投递。
- 经典和 Haru 两套界面共用多级导航目录，父级入口、子级页面和路由由统一目录管理。此轮重点是统一入口与补全功能，不新增整体视觉风格。

已存在的平台公网元数据/公开招聘源与应用内会话采集是不同路径，不能把预设招聘源数量当成接入成功数量。离线 fixture 测试仅证明解析与去重在给定输入下正确，不能证明当前真实平台登录后稳定可用。本人真实登录与线上 WebDAV 账号尚未验收。

## fork 更新与正式发行配置

更新仓库默认指向 `ljy87263621/resume-studio`，默认关闭启动检查更新，可在设置手动检查或开启启动偏好。已有用户保存的启动检查偏好仍按本机设置执行。Stable/Nightly 构建发布机制保留，更新功能的签名、SHA-256、长度、包路径与通道校验继续生效。

客户端当前继承上游更新公钥；这只是开发接管过渡配置，不足以签署 fork 的正式发行资源。正式发行前生成维护者自己的 RSA 签名密钥，替换客户端随包公钥，为目标仓库设置仅 Actions 使用的 `RESUMESTUDIO_UPDATE_SIGNING_KEY` Secret，私钥不入库、不写进同步包。先运行 updater/channel/安装回滚验证，再发布匹配项目 Version 的版本标签和发行说明。密钥格式、签名命令与 metadata/index 结构沿用 `scripts/sign-release.ps1`、`scripts/publish-channel.ps1` 与 [更新说明](UPDATES.md)。如果已有 fork 用户安装了继承公钥的开发包，需要发布完整安装包或设计过渡公钥兼容方案，不能假定直接换钥后原安装自动升级。

目标仓库尚未配置自有 Secret 时，签名发布步骤会失败；不要关闭签名验证来规避。Portable ZIP 与传统 Setup 均应保留，并验证干净 Windows x64 机器上安装/启动、Runtime 补装、离线限制、覆盖安装及数据保留。历史上游 GitHub 下载链接并不是 fork 新包的来源。

## 开发检查与交接

开发需要 Windows、PowerShell、.NET 8 SDK、JDK 21；Node.js 仅用于开发/CI 执行浏览提取脚本 fixture，应用运行和最终用户分发不依赖 Node.js。Windows GitHub Actions runner 已有 Node.js，测试入口会检查其命令可用性，不可用即失败。优先读取 JAVA_HOME，脚本兼容本机 `.dotnet/dotnet.exe`。系统 Python 用于辅助检查，如新增包按用户要求安装到系统 Python。本轮已完成 Portable 构建与原生导航检查，也探测了 BOSS 公网页面壳及牛客公开 HTML；这些探测不代表本人登录、真实 WebDAV 或干净机器分发已验收。

新增功能统一入口为 `./scripts/test-features.ps1`：运行 Navigation.Tests、PlatformCapture.Tests、Sync.Tests、Node.js CaptureScript.Tests.js 和 Java SyncStoreTest。先编译当前后端；如果已执行 test-offline.ps1，可使用 `-ReuseBackendTestClasses` 复用它编译的测试类。离线已恢复 NuGet 项目可以加 `-NoRestore` 避免还原；首次需要先准备项目资产。Sync.Tests 包括模型未知字段往返与两套真实 Java 存储/实际 HTTP DAV 集成，不只使用内存假实现。build/stable/nightly 工作流均在原有测试之后运行此入口，任意非零退出码阻止后续打包/发布。完整发布仍需原有 updater、channel、schedule、installer 等验证。

后续验收重点：在已通过的 Portable 构建与 41 项原生双界面导航检查基础上，继续检查编辑保存/返回/主题切换后的行为；使用用户本人正常登录核对 BOSS/牛客采集、取消与重复收藏；配置支持强 ETag 的真实 WebDAV 验证首次关联、两端修改、删除、网络中断和备份恢复；最后完成自有签名配置及干净机器分发验收。未完成的项目应继续如实列为未验收。

## 本轮最终验收（2026-10-14）

重新执行的离线后端、功能集成、日程、更新器、安装回滚和签名通道测试均通过；原生客户端验收为 41 项，覆盖两套导航、WebDAV 入口、冲突弹窗、本机密码保护、凭据排除和同步关闭等待。安装回滚测试将旧程序替身改为可自行退出的 where.exe，避免恢复后的 cmd.exe 等待输入阻塞测试输出管道。测试证据保存在忽略目录 artifacts/acceptance/resumed-tests.log 与 resumed-native/navigation-result.json。真实平台登录、真实 WebDAV 和干净 Windows 分发仍未验收。
