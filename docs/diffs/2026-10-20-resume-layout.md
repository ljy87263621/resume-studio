# Windows 简历排版与工作台：上游 PR diff 说明

## 比较基线与范围

- 目标仓库：`scj040921/resume-studio`，目标分支：`main`。
- 基线提交：`ee72bc64088bc62d76dd64c3dcd54629b44e7637`。2026-10-20 经 GitHub API 核对，该提交为上游最新 `main`。
- PR 分支：`ljy87263621:Beta/upstream-layout-capabilities`，从该上游基线单独整理。
- 能力参考：`ljy87263621/resume-helper` 的 `6b1b69cf3bd2071c6f3cc17aeea41b492108ac80`；沿用现有 WinUI 3、Java 本地服务和 HTML/CSS 渲染架构，保留现有第三方许可。

本次提交 Windows 排版与工作台补全，以及其依赖的照片／JSON 文件对话框修复。Fork 专用更新地址、公钥、安装器发行者、发布脚本与 Secrets 均不进入本 PR；上游的更新信任配置保持原样。同步确认已有独立 PR #3，本 PR 不包含该功能及其他 WebDAV 改动。

复核命令：

```powershell
git diff --stat ee72bc64088bc62d76dd64c3dcd54629b44e7637...HEAD
git diff ee72bc64088bc62d76dd64c3dcd54629b44e7637...HEAD
```

## 用户可见的变化

| 场景 | 原有行为 | 本次行为 |
| --- | --- | --- |
| 工作台 | 模块目录与编辑、预览空间固定分配 | 工作区宽度 ≥1100 时显示目录、A4 预览、编辑器三栏；较窄时切换编辑／预览；<650 时用模块选择框节约空间 |
| 预览 | 预览容器宽度影响文档显示 | 文档保持 210 mm 宽，视口仅缩放；调整窗口不改变打印 A4 尺寸或容量估算 |
| 页面设置 | 字体、字号、颜色、行距 | 增加上下／左右边距、模块间距、头部左对齐／居中／模板默认 |
| 基本信息 | 固定的姓名、电话、邮箱等字段 | 增加可编辑的 Emoji、标签、内容组合，空内容不输出 |
| 模块录入 | 直接编辑 Markdown，提供格式按钮 | 增加条目、经历、一级目录子板块、图片和表格原生录入入口；正文仍可直接编辑 |
| 表格 | Markdown 文本表格 | 光标定位已有表格后逐格编辑，增删行列；保留管道符、反斜线和多行单元格 |
| 模块图片 | 缺少正文图片录入 | 本地选图、缩放压缩、标题／替代说明、宽高上限、替换与删除；引用标记决定正文位置 |
| Word | 基本可编辑文字导出 | 新增自定义联系项、子标题、可编辑表格和嵌入媒体；应用页面边距等排版参数 |
| 照片与 JSON | 使用 WinRT 文件选择器 | 使用有所属窗口的桌面 Common Item Dialog，兼容未注册 Windows App 的目录分发；补齐取消与导入导出验收 |

## 文件级 diff 导读

| 文件／模块 | 主要责任 |
| --- | --- |
| `client-winui/Models.cs` | 兼容的可选排版字段、自定义信息项和模块图片模型；模块填写状态 |
| `client-winui/MainWindow.Editor.cs`、`MainWindow.EditorLayout.cs` | 工作台布局、窄屏切换、模块目录和排版控件 |
| `client-winui/MainWindow.ContentTools.cs`、`MainWindow.EditingTools.cs` | 内容录入、表格编辑、图片处理、自定义基本信息 |
| `client-winui/Services/ResumeLayout.cs` | 表格往返、图片引用、输入约束、最终文档体积计算等纯逻辑 |
| `client-winui/Services/ResumeRenderer.cs`、`MainWindow.Preview.cs`、`Services/ResumeTypography.cs` | 预览／PDF 共用排版、A4 缩放、字体图片加载后的容量更新 |
| `client-winui/Services/DesktopFileDialog.cs`、`MainWindow.cs` | 桌面文件对话框、照片导入与简历切换保护、JSON 往返；错误消息为空时显示异常类型与 HRESULT |
| `client-winui/Services/BackendClient.cs`、`MainWindow.Updates.cs` | 识别 `--picker-test`，使用隔离数据目录并抑制测试期间的自动更新检查 |
| `client-winui/MainWindow.Appearance.cs`、`MainWindow.Assistance.cs` | 窗口变化后的布局更新与内容提示 |
| `backend/.../ResumeLayout.java`、`Server.java` | 服务端校验可选字段与本地图片数据；Java 17 兼容的共享数值逻辑 |
| `backend/.../ResumeDocx.java`、`ResumeWord.java` | 将导出接入原生 OpenXML 段落、表格及媒体打包，不新增运行库 |
| `scripts/build-android.ps1` | 加入新增共享类；将 Windows 新 CSS 参数解析为 Android 原有默认值；残留占位符时构建失败 |
| `tests/Layout.Tests/`、`ResumeLayoutTest.java` | 旧数据、安全渲染、表格往返、图片大小、字段约束、Word 媒体与 XML 回归 |
| `MainWindow.LayoutVerification.cs`、`MainWindow.WorkbenchVerification.cs` | 原生控件、14 套模板、图片旋转、宽窄视图、实际 PDF 与 JSON 回归 |
| `MainWindow.PickerVerification.cs`、`scripts/test-file-dialogs.ps1` | 原生文件窗口选择／取消、扩展名、照片持久化与切换保护 |
| `scripts/test-workbench.ps1`、`scripts/test-offline.ps1` | 将排版回归接入已有验证入口 |
| `README.md`、`docs/DEVELOPMENT.md`、排版计划文档 | 操作方法、扩展字段、兼容边界和验收入口 |

## 数据结构与兼容性

无需批量迁移旧数据。新增字段均可省略，C# 模型与渲染／导出提供旧文档默认值，Java 保存时校验显式提供的字段。

| 字段 | 默认值／约束 |
| --- | --- |
| `pageMarginVertical` | 14 mm，允许 8–25 mm |
| `pageMarginHorizontal` | 16 mm，允许 8–25 mm |
| `sectionSpacing` | 4.8 mm，允许 0–12 mm |
| `headerAlignment` | `template`；可选 `left`、`center` |
| `profile.items[]` | 空列表；最多 16 项，每项包含 `emoji`、`label`、`value` |
| `sections[].images[]` | 空列表；每模块最多 12 张，包含 `id`、`dataUrl`、`alt`、`caption`、`widthPercent`、`maxHeightMm` |
| 图片尺寸 | 宽度默认 100%，允许 10–100%；高度默认 60 mm，允许 20–170 mm |

正文保持 Markdown：`### 子板块` 表示一层子目录，`![图片](resume-image:<id>)` 独占一行引用当前模块内的图片。新增字段继续进入 JSON、历史副本与同步数据；本次不更改同步协议。

模块图片仅允许有大小约束的 PNG／JPEG／WebP data URL，不请求远程图片；文字、标签及标题按输出格式转义。Windows 本地导入会限制源文件／像素数量、处理 EXIF 方向并缩小图片。透明图优先保留 PNG，超出预算时转为白底 JPEG。替换图片按替换后完整 JSON 的 UTF-8 字节数判断，失败时恢复原图。单份简历继续受现有 2 MB 限制。

## 呈现边界与审阅注意事项

- Android 保留这些 JSON 扩展字段，并能编译共享校验／Word 逻辑；未新增对应触屏录入控件，HTML 预览仍采用原有布局默认值。完整排版与正文图片呈现以 Windows 为准。
- PDF 使用预览的模板 HTML/CSS；Word 输出可编辑的独立文档，不保证与 PDF 的模板装饰、双栏和分页完全一致。WebP 在不同 Word 阅读器中的支持需以目标软件为准，Windows 本地选图会输出 PNG／JPEG。
- 容量估算等待字体和图片加载，不能替代实际打印分页；原生验收包含真实 PDF 页数和非空白检查。
- 不新增前端框架、运行时包、网络依赖或 Windows App 注册要求；沿用现有 Portable／传统安装器分发。
- 未发布 Release；PR 的 GitHub CI 结果以实际运行状态为准，不以本地 Debug 验收替代精简 Release 的完整验收。

## 本次上游分支验证

以下为针对本 PR 独立上游基线的重新验证结果。原始 Fork 分支的历史验收另见排版计划文档。

| 检查 | 结果 |
| --- | --- |
| Windows Debug 目录分发构建 | 通过，0 警告、0 错误；选择器修复后重新编译通过 |
| 纯 C# 工作台与排版回归 | 19 + 16 项通过 |
| Java 离线／Word／模板回归 | 通过，包括 8 项新增排版／Word 检查与 462 项模板目录字段检查 |
| 原生工作台／PDF 验收 | 最终 72 项通过，包含模块重排／改名后的窄屏选择器回归、14 套模板、A4 容量与真实 PDF、JSON 往返 |
| 原生文件对话框 | 选择、取消、默认扩展名、JSON 往返、照片压缩／持久化及简历切换保护均通过 |
| Android 完整 APK 构建与签名验证 | 通过，APK Signature Scheme v2／v3 校验通过；有第三方 D8 调试信息提示 |
| 上游更新器／通道发布回归 | 33 项更新器检查、事务安装／回滚及签名通道脚本检查通过 |
| 与上游的更新信任配置、同步代码比对 | 一致，无 Fork 身份和同步改动混入 |
| 独立只读审查 | 已完成；修复窄屏模块选择框的标题／顺序快照问题，复审无剩余代码问题 |
| `git diff --check` | 通过 |

窄屏选择器回归先在未修复实现上复现失败，再验证修复后的重排、改名与实际选择行为。原生改名检查等待 WinUI 的异步 `TextChanged` 事件完成；此事件时序见 [Microsoft 文档](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.textbox.textchanging?view=windows-app-sdk-1.8)。恢复测试内容后等待预览刷新，再比较不同视口下的 A4 容量。

主要复现入口：

```powershell
./scripts/build.ps1 -Configuration Debug -ReleaseDirectory artifacts/upstream-pr-verified
./scripts/test-workbench.ps1
./scripts/test-offline.ps1 -ReleaseDirectory artifacts/upstream-pr-verified
# 原生入口使用 RESUMESTUDIO_TEST_DATA / RESUMESTUDIO_TEST_OUTPUT 指定全新隔离目录
./artifacts/upstream-pr-verified/ResumeStudio.exe --workbench-test
./scripts/build-android.ps1 -Sdk <AndroidSdk目录> -Output artifacts/upstream-pr-android-smoke.apk
```

中文目录触发 SDK 路径问题时，可按 `docs/DEVELOPMENT.md` 临时映射空闲盘符后运行。上述 APK 为本地签名构建验证产物，未作为上游或 Fork Release 发布。
