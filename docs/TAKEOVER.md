# 职位采集、导航与同步集成说明

PR #1 由 ljy87263621 提供，最初基于 e71aa3d。集成保留 scj040921/resume-studio 的官方更新仓库、签名公钥、发布通道及默认启动检查偏好，并合入 main@58594d8 的工作台、存储维护、投递与日程联动。

## 功能和数据

- 经典与 Haru 界面共用多级导航；发现职位下提供主页面和公司来源管理。
- BOSS／牛客通过本人可见的 WebView2 会话采集返回的职位 JSON、初始数据与可见 DOM。每次最多采集 500 条、确认保存 50 条；保留原链接，使用主分支的原子收藏接口去重，保护已有备注。
- WebDAV 手动同步简历、照片、历史、收藏、投递快照与时间线、公司来源、日程及白名单设置。Cookie、API Key、同步凭据留本机。同步包为用户服务器上的明文 JSON。
- 整包替换传播删除。强 ETag、条件 PUT 和本机 CAS 拒绝过期操作；两端都有变化时明确选择方向。导入前保存 sync-backups，事务日志支持启动恢复，详见 [同步协议](SYNC-PROTOCOL.md)。

## 开发验证

先运行 scripts/build.ps1 和 scripts/test-offline.ps1，再运行 scripts/test-features.ps1 -ReuseBackendTestClasses。新入口覆盖导航、平台解析、浏览提取脚本、扩展字段往返、两套 Java 数据目录的 HTTP WebDAV 集成、超时／取消、冲突、删除、备份与恢复。Node.js 仅用于开发测试，最终应用不依赖 Node.js。

scripts/test-navigation-ui.ps1 在隔离数据目录运行原生导航和设置验证；--workbench-test 验证既有工作台及投递／日程行为。正式发布仍需原有更新器、签名通道和安装器检查。所有 build/stable/nightly 工作流保留新旧测试入口。

## 尚需验收

离线 fixture 和本机 HTTP WebDAV 测试不能证明真实平台的登录状态、页面变化或线上 WebDAV 兼容性。本人真实 BOSS／牛客登录、真实 WebDAV 服务和干净 Windows 分发仍需验收；本次集成不代表发布正式版本。Portable 依赖机器上的 WebView2 Runtime，Setup 在缺失时补装。