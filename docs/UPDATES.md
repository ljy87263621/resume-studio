# Stable / Nightly 更新与发布

设置 → 软件更新可选 Stable、Nightly 以及自动、直连、公益代理三种网络方式。默认 Stable，启动时后台检查；不自动安装。用户确认下载后，校验通过才关闭并更新程序。简历、职位、投递记录和密钥仍在原本的数据目录。

Stable 使用 GitHub `releases/latest`，只接受正式版本。Nightly 从发行列表中选择 `nightly-日期-提交` 预发行版，按提交和构建时间比较，同一提交不会重复提示更新。切换到 Stable 时可回到正式版；不会自动把更高版本的 Stable 换成旧 Nightly。

## 公益代理与校验

内置 `https://gh-proxy.org/`，依据 [服务使用说明](https://gh-proxy.com/docs/quick-start)。直连失败后备用，亦可手动选择代理或仅直连。代理不接收简历、API Key 或 GitHub 账号令牌。元数据、安装包均为公开资源。

`release-info.json` 包含通道、版本、源码提交、构建时间、标签、文件名和 SHA-256。`release-info.sig` 为该文件原始字节的 RSA-PSS / SHA-256 签名。公钥随应用分发；私钥只在 GitHub Actions Secret `RESUMESTUDIO_UPDATE_SIGNING_KEY` 和不入库的本机管理文件中保存。签名失败、通道不符、摘要不符或包路径异常都会停止更新。

安装器保留原程序为同级 `.resumestudio-backup-*` 目录，并保留原目录中未知和修改的额外文件。成功启动由客户端本地服务就绪后确认，失败则恢复旧程序目录。备份和下载缓存暂不自动清理，用户需要恢复旧程序时可使用备份；不要将个人文档放到程序目录。

## GitHub Actions

| 流程 | 触发 | 结果 |
| --- | --- | --- |
| Windows build | main 推送、PR、手动 | 对该提交自动构建和测试，上传 Actions Artifact |
| Publish Stable | `vX.Y.Z` 标签、手动 | 校验版本与说明，构建、测试、签名，发布正式 Release 并设为 Latest |
| Publish Nightly | 每日 UTC 00:30 / 北京时间 08:30、手动 | main 提交与上次成功 Nightly 相同则跳过；否则构建和发布预发行版，保留 Latest Stable |

Nightly 标签：`nightly-yyyyMMdd-八位提交`；安装包：`ResumeStudio-vX.Y.Z-nightly.yyyyMMdd.八位提交-windows-x64.zip`。日期使用北京时间。计划执行可能延迟，依据 [GitHub 定时工作流说明](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#schedule)。

“实时同步”指提交并推送后的自动编译；尚未提交或推送的本机文件不进入 GitHub 构建。

## 构建与维护

`scripts/build.ps1 -Channel stable|nightly -BuildTag <标签>` 生成随包 `Assets/ReleaseIdentity.json`；该文件不入 Git，避免提交号与时间过期。Stable 项目版本来自 csproj，正式标签必须一致。

`scripts/test-offline.ps1` 验证后端；`scripts/test-updater.ps1` 验证更新协议和隔离安装／回滚；`scripts/sign-release.ps1` 使用 Actions Secret 签署更新信息。私钥轮换需要保留旧公钥兼容或发布过渡版，不能直接换钥使已安装客户端无法验证。

旧 v0.5.0 没有更新器，需先安装 v0.6.0。所有自动构建包仍保留 Java / .NET / WinUI 完整运行环境、许可证和对应 OpenJDK 源码。

## v0.6.0 / v0.6.1 更新失败的恢复

若提示安装目录 “is in use”，旧更新器可能继承了安装目录作为自身工作目录。v0.6.2 同时修复启动工作目录与 PowerShell 工作位置，并加入从安装目录启动的回归测试。

旧版会复制并使用本机原有的更新脚本，第一次升级仍需要一次性修补：关闭软件，将正式 v0.6.2 完整包中的 `updater/Update-App.ps1` 复制到原安装目录的同名文件（建议先备份原脚本），再重新打开软件并检查更新。该脚本在验证更新路径后执行 `Set-Location -LiteralPath $workspace` 和 `[Environment]::CurrentDirectory=$workspace`。也可直接手动安装完整 v0.6.2。个人数据目录不参与程序目录替换。
