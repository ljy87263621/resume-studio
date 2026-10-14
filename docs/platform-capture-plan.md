# 招聘平台采集模块

采用现有 WinUI 3 / WebView2 与本地 Java 数据服务。公开详情已有 HTML / JSON-LD 解析；新增可见平台浏览器，由本人在应用内登录、搜索和分页，自动读取当前浏览会话返回的职位 JSON 与可见职位卡片。Cookie 留在本机 WebView2 用户目录，采集结果只含岗位字段。

实施文件为 `MainWindow.PlatformCapture.cs`、`Services/PlatformCapture.cs`、`tests/PlatformCapture.Tests`。主页面入口由主代理集成 `OpenPlatformCaptureAsync("boss" | "nowcoder", keyword)`。保留原有单页浏览并读取流程。限定同平台 HTTPS 响应、职位路径、JSON MIME、4 MB 上限，忽略登录、账号、聊天与验证响应；URL 规范化用于去重，访问参数单独保留。HTML JSON state 仅解析 JSON，不执行任意脚本。

采集先显示数量和职位摘要，选择后核对并批量收藏；搜索或分页由用户在原站完成。暂停、清空和关闭可停止采集。本次仅实现正常会话增量采集，不承诺站点接口长期稳定。

验证采用真实牛客公开页面结构的匿名化 fixture、BOSS 已知 JSON 字段 fixture、错误域名与非岗位/登录数据/畸形和超限响应等边界测试。本人登录后的 BOSS 与牛客实时采集需要用户人工验收；未登录 fixture 不能代替该验收。

官方资料：
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.webresourceresponsereceived
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2webresourceresponseview.getcontentasync
- https://www.nowcoder.com/jobs/intern/center
- https://www.nowcoder.com/jobs/school/jobs
- https://www.zhipin.com/web/geek/jobs
