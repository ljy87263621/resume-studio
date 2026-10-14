# 开发与验证

## 结构

| 目录 | 用途 |
| --- | --- |
| `client-winui/` | WinUI 3 客户端；`MainWindow.*` 按页面／功能拆分 |
| `client-winui/Services/` | 排版、示例预览、AI、日程、更新和本地服务调用 |
| `client-android/` | 安卓离线客户端、触屏界面、Keystore、PDF/文档导入与 WebDAV；业务代码由原 Java 源构建生成 |
| `backend/src/main/java/com/resumestudio/` | Java 21 本地 HTTP 服务、持久化、模板和职位适配 |
| `installer/` | 中文 Windows 安装器 |
| `scripts/`、`tests/` | 构建、打包与验证 |
| `ThirdParty/` | 原始模板与完整许可 |
| `release/`、`artifacts/` | 可运行程序与验证输出，不入 Git |

## 构建

Windows + PowerShell 7 + .NET 8 SDK + JDK 21。设置 `JAVA_HOME`；脚本优先使用该 JDK，通过 `javac`／`jlink` 打包后端和运行时。

```powershell
./scripts/build.ps1
./scripts/package.ps1
```

`-ReleaseDirectory <目录>` 可指定输出。构建自动生成模板目录与发行标识。Setup 另外使用 Inno Setup；GitHub Actions 自动准备固定工具、验证下载并构建安装包。

## 验证

| 入口 | 验证范围 |
| --- | --- |
| `test-offline.ps1` | 离线职位解析、持久化、模板与请求约束 |
| `test-workbench.ps1` | 示例不污染数据、逐模块替换、顺序和隐藏状态 |
| `test-maintenance.ps1` | 日志区间与校验、缓存归属、备份保留和压缩字节不变 |
| `test-updater.ps1` | 签名、下载校验、隔离安装与失败回滚 |
| `test-schedule.ps1` | 日期边界与日程规则 |
| `test-installer.ps1` | 仅 GitHub 临时 runner：安装、快捷方式、覆盖和卸载数据保留 |
| `--workbench-test` | 隔离目录内的原生顶栏、排序、预览与 PDF；收藏去重／撤销、投递日程联动、失败重试及快速切页 |
| `--feature-test`／`--iteration-test` | 既有原生功能与界面回归 |
| `--capture-layout-test` | 窗口布局、缩放点击、当前详情单条收藏、正文读取、推荐排除、补全去重与返回收藏（本地网页夹具＋真实本地存储） |
| `--favorites-editor-test` | 原生收藏多页／筛选／新增删除，以及编辑后切模板中心、重建和关闭预览 |
| `--lightweight-test` | 精简后的真实 EXE：旧资料扩展字段／数字精度、编辑、PDF、文件选择器、缓存清理保留 Cookie |

原生测试可用 `RESUMESTUDIO_TEST_DATA`／`RESUMESTUDIO_TEST_OUTPUT` 指定隔离目录。文件选择器、实际鼠标拖动、外站登录与投递仍需人工验收。自动编译成功不等于完整人工验收。

Release 使用 `dotnet publish -p:LightweightPublish=true` 部分裁剪，并保留整个应用、JSON、WinUI／WinRT／WebView 交互程序集。JSON 反射与 COM 显式保留，IL2026 诊断不隐藏；新增反射类型或原生交互需在真实精简 EXE 上回归，不能以普通构建代替。构建补齐 XBF／PRI，打包前 `test-lightweight.ps1` 验证资源、原字体／预览、运行时和 150 MiB 预算。Debug 仍使用普通构建。

## 本地服务与数据

服务仅监听 `127.0.0.1`，每次启动使用随机端口和会话令牌；请求需要 `X-Resume-Token`。路由以 `Server.java` 为准，覆盖简历、模板、职位来源、收藏、投递、日程、历史与本地建议。

`favorites/collect` 与 `applications/open` 在 Store 锁内按职位标识及规范链接查找或创建，重复请求保留既有内容；`applications/find` 只查询。客户端的 `SaveForm` 在校验和持久化成功后才关闭；异步列表检查导航序号，避免过期结果写入新页面。

JSON 原子保存；上一版本和最多 20 个历史版本压缩保留，日志轮转。数据与程序目录分开。删除简历会删除其备份和历史，投递中的独立快照保留。

企业公开列表支持腾讯、Arbeitnow、Greenhouse、Lever、Ashby、SmartRecruiters、Workday 和公开 JobPosting 检测。预设来源不等于所有来源已接入；动态、自研、登录后接口可能需要专门适配。只请求公开 HTTPS，不读取浏览器 Cookie、不绕过验证、不自动投递。
