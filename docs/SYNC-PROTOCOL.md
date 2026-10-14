# 拾简同步协议 v1

当前实现为用户手动触发的 WebDAV 整包同步：设置页保存配置后点击“检查并同步”。同步 JSON 协议版本 `schemaVersion: 1` 与应用发行版本独立；升级应用不必改变协议，改变数据集合或不兼容语义才需要协议迁移。机器可读结构见 [JSON Schema](resume-studio-sync.schema.json)，采用 draft 2020-12。

## 文件与数据范围

WebDAV 目录中固定文件名为 `resume-studio.sync.json`，UTF-8 JSON，无压缩、无加密封装。请使用个人可控目录。封装必需字段如下：

| 字段 | 含义 |
| --- | --- |
| `protocol` | 固定 `resume-studio-sync` |
| `schemaVersion` | 固定整数 `1` |
| `snapshotId` / `deviceId` | 非空字符串，最长 100 字符；当前生成 UUID |
| `createdAt` | 带时区时间；客户端解析为 DateTimeOffset |
| `data` | 完整资料快照 |
| `extensions` | 对象；允许保留未来扩展 |

JSON 属性名区分大小写，已知字段使用 camelCase；例如 `title` 是已知字段，`Title` 是独立未知字段，不得覆盖或折叠。封装、extensions 与实体对象允许未知字段。客户端上传时复制已读取的封装，因此保留封装及 extensions 的未知字段；后端原始 JSON 导入导出保留实体未知字段。客户端简历、profile、栏目、收藏、投递、timeline、日程、来源及历史模型通过 JsonExtensionData 保留未知字段，正常读取、编辑与保存也保留这些字段。`data` 必须且只能含 `resumes`、`favorites`、`applications`、`sources`、`schedule`、`history` 六个数组及 `settings` 对象。新增未知集合、未知 settings 字段均被拒绝。

| 五类实体 | 内容与最低必需字段 |
| --- | --- |
| `resumes` | `id/title/templateId/profile/sections`；profile 已知姓名、目标岗位、电话、邮箱、城市、链接、摘要；sections 每项必需 title/content/visible；photo 为可选内嵌 PNG/JPEG data URI |
| `favorites` | `id/title/url`；公司、地点、薪资、描述、平台、原始/去重链接、备注和采集状态等 |
| `applications` | `id/title/url/status`；固定投递 `resumeSnapshot`、`resumeTitle`、timeline、安排、提醒与复盘等 |
| `sources` | `id/name/adapter/url/enabled`；用户保存的公司招聘源或内置源覆盖项；部分 ATS 另需 slug |
| `schedule` | `id/title/kind/startAt/allDay/completed/reminderEnabled/reminderMinutes`；可选 notes/applicationId |

照片内嵌在简历、投递简历快照、历史简历内；当前没有附件集合。PDF、导出文件、预览缓存、日志、WebView2 用户目录、网站 Cookie、登录会话、AI API Key、WebDAV 凭据均不属于同步资料。settings 只允许界面、主题、预览、动效、AI endpoint/model、更新通道/线路及启动检查偏好，具体字段与枚举见 Schema。

每个实体集合最多 5000 条，每条必需安全 `id`（`[a-zA-Z0-9_-]{1,100}`），同集合 id 按大小写不敏感判重，拒绝 `one`/`ONE` 这种 Windows 文件名冲突；不同集合可使用相同 id。history 每项必需 id/resumeId/label/automatic/createdAt/resume；resumeId 必须精确关联快照内存在的简历，内嵌 resume.id 精确等于 resumeId，`(resumeId,id)` 同样按大小写不敏感判重，每份简历最多 20 条。删除源简历后，独立投递 resumeSnapshot 仍可保留。

## 校验与兼容性

同步包及后端快照最大 64 MiB（67,108,864 字节），单条五类实体最多 2,000,000 UTF-8 字节，单条 history 最多 2,100,000 字节；HTTP 导入请求本身也受 64 MiB 限制，因此接近上限的快照加请求封装可能超限。JSON 嵌套深度上限 40；客户端远端封装拒绝重复对象属性。五类实体继续调用 Server.validate / CompanySources.validate，包括模板存在性、栏目上限 30、栏目内容上限 30000、HTTPS 职位 URL、日程字段及招聘源限制。

Schema 详细描述当前客户端生成的可互操作已知字段格式，并允许实体未来字段；它不是运行时校验器的完全替代。同步后端已对已知文本、布尔、整数、数字及嵌套 profile/sections/resumeSnapshot/timeline 加入严格类型检查；字号/行距必须可表示为有限 Double，Int32 字段额外拒绝小数与指数写法。DateTimeOffset/Java OffsetDateTime/Instant 与通用 `format: date-time` 的接受范围也不完全相同。JSON Schema 的 format 在部分验证器中只是注释，需要启用格式校验。UTF-8 字节数、重复属性、Windows id 唯一性、history 关联和每简历数量、动态模板目录与招聘源公共域名约束需运行时校验；不可用 `uniqueItems` 代替按 id 去重。当前普通保存最多保留 100 个 timeline 事件，同步导入检查事件字段类型，但没有对应数组数量或 status/at 语义校验。

## 基线、冲突与删除

客户端对 data 计算 SHA-256：对象键按序排序，数组顺序保留，数字从原始 JSON token 按任意精度系数/指数规范化，避免 decimal/double 舍入掩盖未知字段里的细微数字变化；等值数字表示规范化为同一内容。封装时间、snapshotId、extensions 不参与 data hash。后端导出将实体按 id 排序，history 按 resumeId:id 排序。后端 API 另计算本机 CAS hash，按接口返回值原样提交 expectedHash；不要将两种实现的 hash 或远端版本标识混用。远端版本标识优先为强 ETag，缺失或为弱 ETag 时为完整文件原始字节的 `sha256:<64位小写十六进制>`。该指纹包含封装和扩展字段，是客户端观察标识，不是服务器 ETag。

本机 `sync-baseline.json` 记录 Target/DataHash/ETag/At；Target 由标准化目录 URL 和用户名确定。已有相同快照无需替换；仅本机相对基线变化则上传，仅云端变化则下载。首次关联不同快照、双方都变化、或有基线但云端文件消失都会进入冲突。用户明确选择“用本机替换云端”或“用云端替换本机”，也可取消；这是 **whole snapshot conflict，整套资料显式择一**，没有按实体/字段合并，也没有自动选较新时间戳。云端不存在时不能选择下载，只能取消或明确重新上传。

替换采用集合完整镜像：目标中不在新快照内的实体和历史会删除，因此删除会传播。settings 白名单字段也完整替换；本机非白名单设置（包括密钥）保留。检查后执行前再次读取本机 hash 与云端版本标识，观察到变化即拒绝旧计划；本机导入用 expectedHash 做 CAS。整个流程仅成功校验后建立基线。内容校验模式无法封闭最后一次读取与 PUT 之间的竞争窗口，不能声称具备原子并发保护；请避免多个设备同时同步。

## WebDAV 与本机凭据

填写服务提供的 HTTPS 入口或已有文件夹 URL，新配置自动选择其下 `ResumeStudio/`，已保存配置继续使用原目录；缺失目录使用 `PROPFIND Depth: 0` 检查、无正文 MKCOL 创建并回查确认，405 时回查处理并发创建，409 时提示父目录不存在。不会递归创建服务入口，不跟随重定向。URL 不允许 userinfo/query/fragment 或路径穿越；仅离线测试允许 loopback HTTP。

服务应支持条件 PUT。首次创建用 `If-None-Match: *`；有强 ETag 时覆盖用 `If-Match: <观察到的强ETag>`。缺少强 ETag 时读取完整内容生成版本指纹，覆盖前再读取并比较该指纹；若此时服务器提供了强 ETag，恢复使用该 ETag，否则用 `If-Match: *` 只保证目标仍存在，不保证内容未被另一设备同时修改。本地指纹不发送为服务器 ETag。412 表示条件失败，停止写入；上传后 GET 回读并检查实际字节一致才提交基线。创建目录、复查、上传和回读共用单次操作的 90 秒期限。界面会明确提示内容校验模式的并发限制。

用户名、目录、设备 id 与受保护密码存在本机数据目录 `sync-local.json`；密码由 Windows `DataProtectionProvider("LOCAL=user")` 保护，仅当前 Windows 用户在本机使用，跨机器不迁移。AI Key、Cookie 和同步密码不上传；改变目录或用户名后重新填写凭据。清除本机同步密码只清除本机保存值。

## 备份与恢复

云端导入本机前，后端先把旧 data 快照原子保存到数据目录 `sync-backups/<时间>-<UUID>.json`，另写 `sync-recovery.json` 持久恢复日志，然后替换实体、白名单设置和历史。中断时下次 Store 启动读取日志、验证旧快照并回滚；成功后删除日志。导入使用原子替换，不支持原子替换的磁盘会停止操作。备份目前没有自动保留数量策略，需要自行管理；上传没有生成云端历史备份，云端版本保留依赖用户的 WebDAV 服务。

当前没有专用整包恢复 UI。手动恢复可启动本地后端，读取选定 `sync-backups/*.json`（这是 data，不是远端封装），先 POST `/api/sync/validate` 请求 `{ "snapshot": <备份data> }`，再 GET `/api/sync/snapshot` 获取最新本机 hash，POST `/api/sync/import` 请求 `{ "expectedHash": "<最新hash>", "snapshot": <备份data> }`。全部接口仅在 loopback，需当前会话 `X-Resume-Token`；409 时重新核对本机变化并获取 hash。恢复成功也会产生替换前备份。恢复后通过应用重新检查同步，双方有变化时核对方向。操作前保留整个数据目录副本，关闭其他写入者，不要只拷贝某个实体目录或把 data 直接当远端封装上传。

## 后端与未来 Provider

本地 API：GET `/api/sync/snapshot` 返回 `{snapshot,hash}`；POST `/api/sync/validate` 只校验；POST `/api/sync/import` 校验后按 expectedHash 导入，返回 `{snapshot,hash,backup}`。HTTP 400 表示非法数据，409 表示本机已变化，413 表示请求超限。网络提供者遵循 `ISyncRemoteStore`：

```csharp
Task<SyncRemote?> ReadAsync(CancellationToken cancellationToken);
Task<string> WriteAsync(byte[] bytes, string? expectedETag, CancellationToken cancellationToken);
// SyncRemote = byte[] Bytes + string ETag（历史命名；实际为不透明版本标识）
```

未来 GitHub provider 可以用文件 blob/commit SHA 作为不透明版本令牌，Read 返回内容与令牌，Write 必须以观察到的令牌做原子条件更新，null 表示仅允许创建；创建失败/远端变化必须拒绝。还需验证提交内容、保存凭据在本机及明确私密存储方式。当前只实现 WebDAV，不存在 GitHub 同步 UI 或实际 GitHub provider。

## 验证边界

`scripts/test-features.ps1` 覆盖 Navigation.Tests、PlatformCapture.Tests、Node.js 浏览提取脚本 fixture、Sync.Tests 及 Java SyncStoreTest。同步测试既有内存 provider 单元检查，也有两套真实 Java 本机存储与实际 loopback HTTP DAV 集成，覆盖上传/下载、五类集合/历史/照片/投递快照/时间线、密钥排除、基线后更新和删除、冲突前备份、本机及云端旧计划拒绝、扩展保留；模型往返测试覆盖正常编辑中未知字段及大小写区别，协议测试覆盖数字精度和非法输入。不连接真实 WebDAV 服务。已构建 Portable 并完成 32 项原生双界面导航检查；公网探测读取了 BOSS 页面壳和牛客公开 HTML。当前仍未进行用户本人网站登录、真实 WebDAV 服务、跨机器 Windows 凭据或无开发环境机器验收，不能把本机模拟通过描述为线上登录后全流程已验证。
