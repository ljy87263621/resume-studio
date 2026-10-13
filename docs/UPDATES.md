# 更新与发布

“设置 → 软件更新”可选择 Stable／Nightly 和自动／直连／公益代理。启动时后台检查，下载和安装需要用户主动操作。

## 通道

- **Stable**：正式发行版。
- **Nightly**：每日北京时间 08:30 检查 main；源码与上次成功发行相同则跳过，否则发布测试版。计划任务可能延迟。
- 每次推送 main 自动构建并测试；未提交或未推送的本机文件不会进入发行包。

两条通道优先读取仓库 Release `update-channels` 下的 `stable.json`／`nightly.json`，避免匿名 REST API 配额。只有网络读取失败才回退 API；签名或内容无效直接停止。同一 Nightly 提交不会重复提示，切换 Stable 可回到正式版。

## 校验与恢复

元数据以原始字节和 RSA-PSS／SHA-256 签名封装；客户端校验公钥、通道、版本／标签／提交关系，再核对下载大小与 SHA-256。Setup 的名称、大小与摘要也纳入签名。

内置 `https://gh-proxy.org/` 作为公开发行资源的备用线路，不发送简历、AI Key 或 GitHub 登录令牌。签名公钥随应用提供，私钥仅保存在 Actions Secret `RESUMESTUDIO_UPDATE_SIGNING_KEY` 和不入库的本机管理文件中。

更新在独立工作目录运行，事务替换程序，保留个人数据及未知／修改过的额外文件。启动失败时恢复原程序；成功启动由本地服务就绪后确认。备份与下载缓存暂不自动清理。

## GitHub Actions

| 流程 | 触发 | 结果 |
| --- | --- | --- |
| Windows build | main 推送、PR、手动 | 构建、测试、上传 Artifact |
| Publish Stable | `vX.Y.Z` 标签、手动 | 验证版本与说明，发布正式 Release 并设为 Latest |
| Publish Nightly | 定时、手动 | 源码变化时发布预发行版，保留 Latest Stable |

两条发行均提供 Setup、便携 ZIP、签名元数据、SHA256SUMS 和对应 OpenJDK 源码。全部上传成功后刷新通道索引。索引短暂不可用时允许网络回退，不将失败伪装为“没有更新”。

## 旧版恢复

旧版第一次升级若遇到 API 403、缺少更新器或安装目录 “is in use”，直接使用最新 Setup 覆盖安装。个人数据目录不参与程序替换。v0.6.0／0.6.1 也可用新版 `updater/Update-App.ps1` 备份后替换旧脚本，再在软件内重试。
