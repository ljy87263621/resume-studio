# 更新与发布

“设置 → 软件更新”可选择 Stable／Nightly 和自动／直连／公益代理。启动时后台检查，下载和安装需要用户主动操作。

## 通道

- **Stable**：正式发行版。
- **Nightly**：每日北京时间 08:30 检查 main；源码与上次成功发行相同则跳过，否则发布测试版。计划任务可能延迟。
- 每次推送 main 自动构建并测试；未提交或未推送的本机文件不会进入发行包。

两端分别读取仓库 Release `update-channels` 下的 `desktop-stable.json`／`desktop-nightly.json` 与 `android-stable.json`／`android-nightly.json`，避免匿名 REST API 配额。只有网络读取失败才回退 API；签名或内容无效直接停止。同一 Nightly 提交不会重复提示，切换 Stable 可回到正式版。

## 校验与恢复

元数据以原始字节和 RSA-PSS／SHA-256 签名封装；客户端校验公钥、通道、版本／标签／提交关系，再核对下载大小与 SHA-256。Setup 的名称、大小与摘要也纳入签名。

内置 `https://gh-proxy.org/` 作为公开发行资源的备用线路，不发送简历、AI Key 或 GitHub 登录令牌。签名公钥随应用提供，私钥仅保存在 Actions Secret `RESUMESTUDIO_UPDATE_SIGNING_KEY` 和不入库的本机管理文件中。

更新在独立工作目录运行，事务替换程序，保留个人数据及未知／修改过的额外文件。启动失败时恢复原程序；成功启动由本地服务就绪后确认。启动后清理已完成更新的下载与解压载荷，仅保留最近一份完整程序备份；额外文件和修改过的文件保留。

## GitHub Actions

默认发布顺序为 **Nightly → 验收 → Stable**：先提交目标版本号和发行说明，手动运行 Publish Nightly，核对构建、签名、安装包及目标功能，再将同一源码提交标记为正式版本发布 Stable。“发布新版本”默认先发布 Nightly；尚未完成真实服务验收时，应在测试版说明中明确范围，不能仅凭编译或离线测试通过就直接升为 Stable。Stable 发布需用户明确要求或确认验收；明确的正式版发布指令优先于默认流程。

| 流程 | 触发 | 结果 |
| --- | --- | --- |
| Windows build | main 推送、PR、手动 | 构建、测试、上传 Artifact |
| Publish Stable | `vX.Y.Z` 标签、手动 | 验证版本与说明，发布正式 Release 并设为 Latest |
| Publish Nightly | 定时、手动 | 源码变化时发布预发行版，保留 Latest Stable |

两条发行均提供 Setup、便携 ZIP、签名元数据、更新日志、SHA256SUMS 和对应 OpenJDK 源码。全部上传成功后刷新通道索引。索引短暂不可用时允许网络回退，不将失败伪装为“没有更新”。

检查更新时读取对应发行的 release-changelog.json，按最新版本到当前版本展示说明，当前版本标为基线。已经最新时仍可查看该版日志；同版本 Nightly 展示本版本累计说明。日志大小与 SHA-256 纳入签名，读取失败会提示且不绕过程序包校验。

## 旧版恢复

Android `0.9.5-nightly.20261015.ede43b32` 发行包缺少更新验证公钥，检查更新会提示 `UpdatePublicKey.pem`。请从 GitHub Releases 手动下载较新的 Nightly APK 覆盖安装一次，无需卸载。修复后的构建会检查 APK 中的公钥与电脑端受版本管理的公钥一致；签名与摘要校验继续保留。选择 Nightly 通道可接收后续测试版，Stable 通道只检查正式版。

旧版第一次升级若遇到 API 403、缺少更新器或安装目录 “is in use”，直接使用最新 Setup 覆盖安装。个人数据目录不参与程序替换。v0.6.0／0.6.1 也可用新版 `updater/Update-App.ps1` 备份后替换旧脚本，再在软件内重试。

## 安卓和桌面独立发行（0.9.9 起）

- 桌面版本由 `client-winui/ResumeStudio.csproj` 管理；桌面工作流 `nightly.yml`／`stable.yml` 只构建、签名和发布 Windows ZIP／Setup。保留 `vX.Y.Z`、`nightly-日期-提交` 标签。
- 安卓版本由 `client-android/AndroidManifest.xml` 管理；`android-release.yml` 的 channel 选择 Nightly 或 Stable，只构建和发布 APK。标签为 `android-vX.Y.Z`、`android-nightly-日期-提交`，元数据为 `android-release-info.json`／`.sig`。
- 两端独立更新四个带平台前缀的索引，不互相覆盖。安卓 Stable 的版本码以清单为准，Nightly 根据安卓版本、日期和当日序号生成；不再读取桌面版本号。
- 更新通道、下载线路和启动检查选项只保存在各自本机，不通过 WebDAV 相互覆盖。旧同步文件中的这些字段仍能校验和读取，但导入时忽略。
- Android Stable 必须先通过同版本、同提交的 Android Nightly 验证；桌面 Stable 仍只验证桌面 Nightly。安卓发行不改变 GitHub Latest 桌面正式版。
- 定时发布只在各自依赖的源码变化后创建新 Nightly；共享后端、模板或字体变化可同时触发两个平台。

### 旧客户端迁移

0.9.8 及以前读取无平台前缀的 `stable.json`／`nightly.json`。首次发布拆分版本后，同提交、同基础版本、同日的双端发行可手动运行 `platform-migration.yml`：验签并合成一次旧协议桥接发行，让旧客户端正常收到迁移更新。该操作仅改旧索引，不改四个独立索引。桥接包包含同签名 APK，升级后各端自动进入独立通道；以后单端发布不再碰旧索引。两个平台版本已经分叉时不能运行此桥接，脚本会拒绝。

尚未运行迁移时，旧客户端仍停留在旧索引；请勿将独立分发误报为旧客户端已经收到更新。首次安卓独立发行前，应确保 `update-channels` Release 已存在（现有仓库已具备）。
