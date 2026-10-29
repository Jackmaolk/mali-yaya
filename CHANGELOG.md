# 更新日志 / Changelog

## 未发布：2026.10.28.5 候选版 / Unreleased Candidate

- 首次启动默认开启 Tibo 自动监测：检查公开动态、核对作者与帖子，并按需获取中文机译。已有手动关闭选择不会被覆盖；损坏或不安全的设置仍停止联网。
- 新增首次配置、已有目录缺失设置、保留关闭、空配置等回归测试；离线界面测试显式关闭联网，不依赖产品默认值。
- 同步中英文使用说明和隐私说明。自动更新仍默认关闭，本次没有扩大 GitHub 更新权限。
- 使用说明新增醒目的右键菜单入口提示及中英文完整菜单表，说明 CLI 识别/选择、连接信息、更新配置/状态、Tibo 开关和极简模式；区分只读状态行、面板按钮和独立联网开关。
- 本机桌面启动失败已通过独立安装目录与用户级 .NET 10 运行库修复；未更改系统 .NET、全局代理或凭据。通用 ZIP 仍要求 .NET 10 Desktop Runtime x64，并非自包含包。

本次完整自动化测试：691 通过，0 失败，0 跳过；候选包清单/哈希、禁止覆盖及版本不匹配检查全部通过。依赖漏洞数据仍不可用，不视为安全审计通过。

First-run Tibo monitoring is now on. Saved opt-outs survive upgrades, malformed settings stay offline, and offline tests explicitly disable networking. Public-source delays and verification failures remain possible; this is not an official real-time X feed or a guaranteed reset prediction. This candidate is not a published stable release.

## 未发布：2026.10.28.4 候选版 / Unreleased Candidate

本轮代码保存在独立功能分支 `feat/release-readiness-net10`。以下是已实现并经过本地验证的变更，不表示已经上传、合入 main 或发布正式下载包。

### 修复

- 修复首次读取周额度为 100% 时，错误提示“刚完成重置”。首次读取现在仅建立基线；只有后续观测到有效的额度恢复或可信新周期变化才确认重置。
- 修复可选设置、缓存或数据目录异常可能影响启动的问题：无效路径、网络路径和可检测的重解析点安全降级，不回退写入程序目录。
- 修复关闭自动更新后，已启动的更新助手仍可能在稍后关闭组件时替换程序的问题。助手就绪不等于获准安装，必须经过界面再次确认的一次性交接许可。
- 修复旧更新会话的迟到通知覆盖“自动更新已关闭”等新状态的问题。
- 统一当前界面的产品名称为“码力芽芽 / Mali Yaya”，保留原创芽芽角色。

### 升级与新增

- 迁移至 .NET 10：应用、测试、SDK 选择、程序集版本、发布清单和更新包运行时校验同步调整。
- 设置、运行数据和缓存迁移至当前用户 LocalAppData。按安装目录隔离健康记录，并将最小健康状态与账户额度/译文分开。
- 不自动导入旧的私人账户缓存或无账户绑定的倒计时。新启动以新鲜账户数据为准，同次运行断网时保留可靠的倒计时终点。
- 新增独立的 Tibo 联网监测与翻译开关，首次使用默认关闭。关闭时取消查询与重试、拒绝迟到结果，额度刷新与本地倒计时继续工作。
- 新增可持久保存的极简模式，收起辅助建议和快报；该模式与联网开关相互独立。
- 新增 push / PR Windows CI，保持最小权限、固定 Action 提交和严格的依赖漏洞审计。
- 新增受审查的 main 分支手动候选发行流程，只创建草稿，不自动公开发布或合并 PR。
- README 增加 12 张中英文多状态预览：正常、预告、确认重置、额度偏低、极简、Tibo 联网关闭。均明确标注模拟数据，并与原有授权实机截图区分。

### 已执行验证

| 项目 | 本地结果 |
| --- | --- |
| .NET 10 自动化测试 | 688 通过，0 失败，0 跳过 |
| 运行时配置合成场景 | 41 项通过 |
| 便携启动器隔离场景 | 26 项通过 |
| 图标检查 | 9 个尺寸通过 |
| 发布包检查 | 3 项通过 |
| 源码导出安全检查 | 8 项通过 |

未读取或上传账户凭据、auth.json、私人运行状态或完整日志。没有修改全局代理、登录配置或系统安全设置，没有重启电脑或 Codex。

### 已知限制与发行前要求

- 当前是候选变更，不是已经通过实机验收的正式 Release。
- 本机 NuGet 漏洞源查询遇到 NU1900 / TLS 初始化问题，不能视为漏洞审计通过；远端 CI 保持严格失败门槛，但尚未实际运行。
- 覆盖率探查器未初始化成功，没有可用覆盖率数据，未宣称达到 80%。
- 干净 Windows、真实 Plus/Pro、英文系统与混合 DPI、关闭联网后的进程级流量观察、真实热更新与回滚仍待验收。
- 新版需要 .NET 10 Desktop Runtime x64。旧 .NET 9 更新器不能自动跨主版本升级，必须先准备运行库并手动完整解压新包。
- 不保证代理节点稳定，不提供自动控制机场或修复系统网络的功能；剩余额度也不等于 Codex 请求一定可用。
- 软件许可、代码签名及商业发行条件仍须单独确认，不因仓库公开而自动解决。

完整记录：[升级验证](docs/UPGRADE-VERIFICATION.md) · [实机验收](docs/RELEASE-ACCEPTANCE.md) · [自动更新协议](docs/AUTO-UPDATE.md)。

---

## English Summary

**2026.10.28.4 is an unreleased review candidate**, not an already uploaded, merged or published stable release.

### Fixed

- Initial 100% weekly quota is now a baseline, not evidence of a just-completed reset.
- Optional storage/settings failures degrade safely rather than crashing startup or writing beside the executable.
- A ready update helper cannot install after cancellation or an abandoned handoff. Installation requires explicit, one-shot UI approval.
- Old update-session notifications cannot overwrite a disabled or newer state.
- Current branding consistently uses the original Mali Yaya character and product name.

### Added And Changed

- .NET 10 application/test targets, SDK selection, assembly identity, manifest and strict updater runtime-contract checks.
- Current-user LocalAppData storage, per-install minimal health records, separate private account state and public translation cache. No automatic import of unbound legacy account state.
- Independent, default-off Tibo networking/translation preference with cancellation and stale-response rejection. Quota and countdown continue when it is off.
- Independent persistent minimal mode.
- Least-privilege push/PR Windows CI and a trusted-main, manual draft-candidate workflow; neither merges nor publishes stable releases automatically.
- Twelve labeled offline Chinese/English WPF state previews alongside the existing consented real screenshot.

### Verification And Remaining Gates

688 local tests passed, with no failures or skips. Runtime-contract fixtures (41), launcher scenarios (26), icon sizes (9), package checks (3) and source-export safety checks (8) passed.

Local vulnerability data was unavailable, coverage collection did not initialize, and remote CI has not run. Clean-machine integration, real subscription accounts with consent, English/mixed-DPI display, outbound-network observation and live update/rollback remain pending. No credentials or private account runtime were read or uploaded. No computer/Codex reboot or global security/proxy changes were made.

Old .NET 9 updaters cannot migrate across the runtime major automatically. Prepare Desktop Runtime 10 x64 and manually extract the complete reviewed package first. Licensing/signing and stable-publication approval remain separate gates.
