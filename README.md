# Codex MaliYaya · 码力芽芽

[English](README.en.md) · [下载现有测试版](https://github.com/Jackmaolk/mali-yaya/releases) · [反馈建议](https://github.com/Jackmaolk/mali-yaya/issues/new)

**看额度、追预告、确认重置。让原创芽芽陪你使用 Codex。**

Windows 桌面小组件：五小时翻页倒计时、每周额度、Tibo 公开动态、北京时间预告、账户重置庆祝，中英文界面随系统切换。

独立项目，**非 OpenAI 官方产品**。展示名称加入 Codex 前缀，仓库仍为 `Jackmaolk/mali-yaya`，下载地址、更新源和程序文件名不变。

## 本次更新了什么

**启动/退出热修复：** 修复异步清理同步完成时在 Closing 事件内再次 Close 导致的 WPF 崩溃，以及关闭后 Loaded 回调重启计时器的问题。新增 6 项隔离生命周期测试，包含实际原生窗口加载后立即关闭；不启动 CLI 或 HTTP 服务。旧快捷方式可能仍指向旧版，请完整解压新版并核对入口。

**2026.10.31.1 / .NET 10 Windows x64 测试版。** 修复首次周额度 100% 误报重置、设置异常启动失败和取消更新后助手仍可能安装的问题；迁移 .NET 10 与 LocalAppData；新增独立 Tibo 联网开关、极简模式、push/PR CI 和 15 张运行/状态图片。

737 项自动化测试通过，远端依赖审计步骤通过；真实账号、干净系统、更新回滚和覆盖率仍待验收。详见 **[完整中英文更新日志](CHANGELOG.md)**，不要将候选更新当成已发布稳定版。

**有右键菜单：在小组件标题栏或面板背景处单击鼠标右键，即可设置 CLI 连接、更新仓库、Tibo 联网监测和极简模式。** Tibo 首次使用默认开启；自动更新仍默认关闭，二者是独立开关。

> Windows 桌面上的 Codex 重置情报助手：关注 Tibo 发文、查看北京时间预告、确认本账户重置，再用原创小宠物庆祝。

非 OpenAI 官方产品，无隶属、赞助或背书关系。当前版本为 **2026.10.31.1 / .NET 10 测试版**，不是已完成商用签名、安装和真实自动升级验收的正式安装包。项目仓库：[Jackmaolk/mali-yaya](https://github.com/Jackmaolk/mali-yaya)。测试版需要手动完整解压；自动更新不会安装预发布版本。源码可见不等于已授予开源或商用许可，见 [权利说明](RIGHTS.md)。

## 先看界面：中英文 × 7 种状态

**共 15 张图：14 张中英文状态演示 + 1 张用户授权实机截图。** 状态演示由真实 WPF 界面离线渲染，使用模拟数据，并在图底标注；不是实时账户截图、当前有效预告或权益承诺。

以下演示展示 .NET 10 界面；时间、额度与帖子均为模拟或截图当时的数据，不代表当前账户状态。手机窄屏可逐张阅读，点击图片放大。

### 重置完成 · 芽芽庆祝

<p>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-confirmed-zh.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-confirmed-zh.png" alt="重置完成 · 芽芽庆祝：中文离线界面演示，模拟数据" width="280" /></a>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-confirmed-en.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-confirmed-en.png" alt="Reset confirmed · Yaya celebrates: English offline UI demonstration with synthetic data" width="280" /></a>
</p>

左侧/上方为中文，右侧/下方为英文；点击图片可查看原图。

### 提前预告 · 北京时间

<p>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-planned-zh.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-planned-zh.png" alt="提前预告 · 北京时间：中文离线界面演示，模拟数据" width="280" /></a>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-planned-en.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-planned-en.png" alt="Reset announced · Beijing time: English offline UI demonstration with synthetic data" width="280" /></a>
</p>

左侧/上方为中文，右侧/下方为英文；点击图片可查看原图。

### 联网失败 · 保留已知数据

<p>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-unavailable-zh.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-unavailable-zh.png" alt="联网失败 · 保留已知数据：中文离线界面演示，模拟数据" width="280" /></a>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-unavailable-en.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-unavailable-en.png" alt="Connection failure · Keep last known data: English offline UI demonstration with synthetic data" width="280" /></a>
</p>

左侧/上方为中文，右侧/下方为英文；点击图片可查看原图。

### 日常额度 · 翻页倒计时

<p>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-normal-zh.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-normal-zh.png" alt="日常额度 · 翻页倒计时：中文离线界面演示，模拟数据" width="280" /></a>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-normal-en.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-normal-en.png" alt="Everyday quotas · Flip countdown: English offline UI demonstration with synthetic data" width="280" /></a>
</p>

左侧/上方为中文，右侧/下方为英文；点击图片可查看原图。

### 额度偏低 · 醒目提醒

<p>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-low-zh.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-low-zh.png" alt="额度偏低 · 醒目提醒：中文离线界面演示，模拟数据" width="280" /></a>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-low-en.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-low-en.png" alt="Low allowance · Clear warning: English offline UI demonstration with synthetic data" width="280" /></a>
</p>

左侧/上方为中文，右侧/下方为英文；点击图片可查看原图。

### 极简模式 · 更小的桌面占用

<p>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-minimal-zh.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-minimal-zh.png" alt="极简模式 · 更小的桌面占用：中文离线界面演示，模拟数据" width="280" /></a>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-minimal-en.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-minimal-en.png" alt="Minimal mode · A smaller desktop footprint: English offline UI demonstration with synthetic data" width="280" /></a>
</p>

左侧/上方为中文，右侧/下方为英文；点击图片可查看原图。

### 主动关闭 Tibo · 额度照常刷新

<p>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-offline-zh.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-offline-zh.png" alt="主动关闭 Tibo · 额度照常刷新：中文离线界面演示，模拟数据" width="280" /></a>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-offline-en.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-offline-en.png" alt="Tibo switched off · Quota still refreshes: English offline UI demonstration with synthetic data" width="280" /></a>
</p>

左侧/上方为中文，右侧/下方为英文；点击图片可查看原图。

## 用户提供的实机截图

<img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-reset-complete-zh.png" alt="码力芽芽实际运行：原创芽芽角色、上下翻页倒计时、五小时与每周额度，以及本账户重置完成提醒" width="463" />

用户提供的新版实机截图，非设计稿。左侧为芽芽与上下结构翻页倒计时；右侧显示账户额度、随机重置机会；下方为 Tibo 原文、中文概要与本账户重置完成提醒。

**截图仅代表拍摄时的账户状态。** 百分比、重置机会、套餐、帖子及时间不是新安装默认值、当前有效预告或对其他用户的权益承诺。图中“本账户重置已完成”基于账户数据，不是仅凭英文帖文宣告到账。新版本使用原创芽芽，不使用旧角色形象。

[查看原图](docs/images/widget-reset-complete-zh.png) · [反馈建议或报告问题](https://github.com/Jackmaolk/mali-yaya/issues/new)

反馈时请说明软件版本、Windows 版本、屏幕缩放比例、复现步骤及期望效果。截图只保留必要区域，先遮住邮箱、用户名、私密对话、路径或凭据；不要上传 auth.json、Token、完整运行日志或个人账户状态文件。


## 先看这三点

- **网络是前提**：需要能正常访问 Codex、X 相关公开动态接口和机译服务的网络。在无法直连这些服务的环境中，通常要先开启自己的代理工具，也就是常说的“魔法”。软件不附送代理、节点或订阅。
- **重点是重置情报**：关注 Tibo（[@thsottiaux](https://x.com/thsottiaux)）的公开发文；有有效重置预告时显示预告及北京时间，没有预告时展示最近已核实动态。中文界面在英文原文下提供中文机译概要。
- **预告不是到账**：只有新鲜的账户额度数据满足重置判定，才显示“本账户重置已完成”并庆祝。这里的“重置/赠送机会”不是充值、付款或购买 Credits，不保证所有账号同时收到。

码力芽芽是独立 WPF 小组件，不注入 Codex 页面，不使用 CDP，不修改 app.asar、模型配置或登录文件。新版使用本项目独立绘制的叶片机器人“芽芽”，保留完整身体与状态互动。

界面自动跟随 Windows 显示语言：zh 系列使用中文，英语及其他语言回退到 English；重新启动组件后应用显示语言的改变，不修改系统语言、不增加翻译请求。英文界面保留帖子原文，中文机译概要不强制显示。

新版原创图标已嵌入启动器。个人桌面入口可通过主动运行 InstallDesktopEntry.exe 更新为“码力芽芽.exe”或“Mali Yaya.exe”，原入口先备份；构建默认不写桌面。[图标设计与来源](docs/ICON-DESIGN.md)。

## 主要功能

| 功能 | 实际行为 |
| --- | --- |
| 套餐识别 | 根据本机 CLI 返回的 planType 显示 PLUS、PRO 等；缺失时显示 --，不推测套餐。 |
| 额度卡片 | 显示实际返回的五小时、每周用量及重置时间；不存在的窗口自动隐藏。 |
| 翻页倒计时 | 根据账户 resetsAt 每秒本地计算；刷新失败继续已有计时，到零等待账户的新周期，不自行补五小时。 |
| 随机重置 | 显示账户返回的次数与有效期，不虚构、不自动消耗机会。 |
| Tibo 快报（重点） | 核对限定作者的公开帖子，区分有效预告、最近动态、赠送机会消息和本账户已确认重置，显示英文与中文机译概要。 |
| 重置完成提醒 | 在已有基线后观测到周额度恢复，或可信新周期证据满足规则时更新面板，并触发角色庆祝；不是只等到预告时间再猜。 |
| 原创角色 | 根据额度、预告、赠送和已重置状态变化；支持点击互动和系统减少动画设置。 |
| 桌面操作 | 拖动、折叠、关闭、手动刷新，可选当前用户开机启动；其他窗口可覆盖组件。 |
| 自动更新 | 默认关闭、更新源为空；开启且配置正确后每 24 小时最多尝试一次正式 Release，校验 ZIP 后仅切换本组件。 |

不提供 Token 历史统计、API 账户/Key 管理、重置概率预测或自动发送“继续”。剩余百分比不保证某个模型或当前网络一定可用。

## 1. 开始使用

### 1.1 准备环境

- 支持 .NET 10 且已安装当前安全补丁的 Windows x64；多屏与真实英文系统仍需实机验收。旧 Windows 可能缺少 CET 支持，请勿通过关闭安全保护绕过。
- 候选版需要 [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)，开发需要 .NET 10 SDK；普通 .NET Runtime 或仅 ASP.NET Runtime 不够。旧 .NET 9 测试版不能自动跨主版本升级，须先安装 .NET 10 Desktop Runtime，再手动解压新包。
- 安装并登录你自己的 Codex CLI，或使用可访问的 Desktop/IDE 自带 CLI。仅在浏览器登录不保证 CLI 已登录；API Key 登录不等于 ChatGPT 套餐额度。
- 不需要向组件填写 OpenAI Token、密码或复制 auth.json。不要关闭杀毒软件；若被拦截，先核对来源、校验值与签名状况。

### 1.2 先准备好代理和登录

1. 如当前网络不能直连所需服务，先打开自己的代理工具，连接可用节点，并按该工具的说明启用系统代理。**端口以你自己的工具实际值为准**，不是所有人都用 7897。
2. 检查本机 Codex CLI 或 Desktop 自带 CLI 已登录你自己的 ChatGPT 账户，而且 Codex 能正常完成一次请求。浏览器打开 ChatGPT 不等于本机 CLI 已登录。
3. 再启动组件。如果在组件启动后才修改代理端口或代理环境变量，先正常关闭并重新打开组件，让新的进程使用新的网络环境；不要为了组件强制结束 Codex 或重启电脑。

组件的外部 HTTP 请求使用 Windows/.NET 的默认代理机制；Codex 子进程继承启动环境中的代理变量，具体网络行为也受 Codex 本身影响。如果旧 HTTP_PROXY、HTTPS_PROXY 或 ALL_PROXY 仍指向已停用的端口，仅修改代理软件的端口可能不够。请核对这些配置是否一致，不要把账户凭据写入代理地址。

**本软件不内置自动扫描代理端口、切换机场节点、启停 Clash/LLG 或修复系统网络的功能。** 个人机器上另行配置的网络守护脚本不是本仓库的组件依赖，也不包含在分发包中。端口监听成功、网页可打开或面板保留旧百分比，都不能证明 Codex 的长连接正常。

账号额度、Tibo 动态、机译、GitHub 更新可能分别成功或失败。出现“等待同步”“动态不可用”时，应查看该项的错误/更新时间，不能把旧数据当成本轮读取结果。不要关闭防火墙、杀毒软件或 TLS 证书校验来排查。

### 1.3 下载与启动

[2026.10.31.1 测试版下载](https://github.com/Jackmaolk/mali-yaya/releases/tag/v2026.10.31.1)（Windows x64 / .NET 10；发布准备期间链接可能尚未生效）。这不是已签名的稳定版安装器；使用前请查看发行说明中的待验收项。请只从本仓库下载名称严格匹配的 ZIP，核对 SHA256SUMS.txt 与 GitHub 资产摘要。

1. 完整解压 ZIP 到可写的本机文件夹，保留 EXE、DLL、两个运行配置 JSON 和 release-manifest.json。
2. 双击 CodexUsageWidget.exe；不需要命令窗口常驻，但正常运行仍有后台进程。
3. 第一次读取失败时，右键组件选择“查看 Codex 连接信息”，检查来源、版本和登录。

旧版桌面入口可保留兼容；当前产品名统一为码力芽芽 / Mali Yaya。关闭后可再次双击入口，无需重启电脑或 Codex。

### 1.4 CLI 自动识别

优先使用明确保存的路径，其次检查可信已知位置中的原生 CLI。支持已知 npm、独立版及可访问的 Desktop/IDE 安装。右键可“重新自动识别 Codex CLI”或“手动选择 Codex CLI”。不执行 .cmd/.ps1 shim，不扫描全盘，不自动安装或登录。[详细范围](docs/AUTO-DISCOVERY.md)。

## 右键菜单

在**小组件标题栏或面板背景处单击鼠标右键**打开菜单，不是在桌面 EXE 或快捷方式上右键。菜单随 Windows 显示语言显示中文或英文。

| 菜单项 | 功能与注意事项 |
| --- | --- |
| 重新自动识别 Codex CLI | 清除手动指定的 CLI 路径，重新检查可信已知安装位置并尝试连接；不扫描全盘、不自动安装或登录。 |
| 手动选择 Codex CLI… | 选择可信、架构兼容的原生 CLI `.exe`，不是桌面主程序、快捷方式或 `.cmd` / `.ps1` 脚本；保存后尝试连接。 |
| 查看 Codex 连接信息 | 查看当前 CLI 来源、版本和路径；尚未识别时显示相关提示，不展示或导出凭据。 |
| 自动更新（每 24 小时检查） | 勾选开启、取消勾选关闭；默认关闭。必须先配置本软件的具体更新仓库，开启后每 24 小时最多尝试一次，包括失败的尝试。 |
| 配置本软件的更新仓库… | 填写 `Jackmaolk/mali-yaya` 这样的具体仓库名，不填个人主页或 Token；保存后保持自动更新关闭，需要再手动勾选开启。 |
| 查看更新状态 | 显示当前版本、配置的仓库、更新开关和最近状态；这是信息窗口，不是绕过 24 小时间隔的“立即更新”按钮。 |
| Tibo 联网监测（含翻译） | 勾选开启公开动态查询、帖文核对及按需机译；首次使用默认开启。取消勾选会停止这部分联网，不停止账户额度刷新和本地倒计时。 |
| 极简模式 | 勾选后收起快报、建议及辅助行，保留主要额度、芽芽和倒计时；取消勾选恢复完整面板，不改变 Tibo 联网开关。 |

菜单还包含一行不可点击的更新状态提示，其文字会随更新状态变化。Tibo 和极简模式分别保存，下次启动继续使用已保存的选择；升级不会覆盖主动关闭 Tibo 的选择。

**刷新、折叠和退出不是右键菜单项**，请使用面板顶部对应按钮；开机启动使用面板底部复选框。折叠或极简模式不等于停止后台监测。

## 联网开关与极简模式

右键 → **Tibo 联网监测（含翻译）**：首次安装默认开启，启动后自动检查公开动态并核对帖文、按需机译；已保存的手动关闭选择不会被升级覆盖，配置损坏或不安全时停止联网；关闭会取消在途请求及重试、拒绝迟到结果，不影响账户额度和五小时倒计时。已发送的请求无法撤回，关闭也不能删除第三方服务器记录。

右键 → **极简模式**：只显示主要额度、芽芽和倒计时，收起快报、建议与辅助行；联网开关独立保存。极简不是断网，折叠也不是关闭联网。

## 2. 日常使用

| 操作 | 说明 |
| --- | --- |
| ↻ | 立即刷新账户额度；网络失败保留旧数据，并显示失败状态。 |
| ⌃ | 折叠/展开，不会关闭监测。 |
| × | 关闭本组件；关闭后不会继续检查帖子或更新。 |
| 角色 | 悬停/点击产生轻量反馈，不会使用重置卡。 |
| Tibo 卡片 | 显示最近动态或有效预告，点击打开原帖；中文为机译概要。 |
| 开机启动 | 可选当前用户登录后启动；不是系统服务。 |

时间在面板中按北京时间（UTC+8）展示。公开帖子中的相对时间必须结合发帖时间和时区判断；模糊预告保留不确定性，不能当作官方承诺。账户用量和新周期才是本账户已重置判断的依据。

## 3. 数据来源与刷新

### 3.1 Tibo 发文怎样变成重置提醒

组件不是直接登录 X、遍历全部帖子或调用模型猜时间。当前实现从社区接口获取候选动态/时间窗口，再通过 FxTwitter/VxTwitter 等公开镜像核对帖子作者、链接、正文与发布时间。只接受受约束的 `x.com/thsottiaux/status/...` 原帖链接；不保证每条最新帖子都能被第三方接口及时收录。

在核实原帖的基础上，结合社区提供的有效时间窗口显示预告。关键词匹配、来源时间和窗口有效性都参与判定；“明天”“今晚”等模糊文字没有可靠时间依据时，不应被当作精确到分钟的承诺。社区预测不等于 Tibo 对所有用户的保证，也不代表他已操作了某个具体账户。

| 面板状态 | 含义与使用建议 |
| --- | --- |
| Tibo 已预告重置 | 有可核实的重置相关帖子和当前有效窗口；可考虑优先推进有价值的任务，但不要为了预测耗尽额度。 |
| Tibo 提到赠送重置机会 | 原帖提到赠送等信息；是否到账、次数及有效期仍看账户接口。赠送卡可能需要用户自行在官方界面使用。 |
| 社区观察到重置 / 核对账户中 | 公开动态报告了恢复，但本账户尚缺确认；不会据此声称额度已到账。 |
| 本账户重置已完成 | 已取得符合规则的新鲜账户数据，更新本轮状态并触发一次角色庆祝；继续追踪之后的新帖子。 |
| Tibo 最新/最近已核实动态 | 没有有效预告时展示可核实的最近帖子、发布时间、原文与中文机译概要；缓存内容不冒充新抓取。 |
| 暂不可确认 / 预告已过期 | 接口失败、原帖无法核实或窗口过期；保留不确定性，不继续把旧预告说成即将发生。 |

这里的提醒是**组件卡片、状态文案和角色动作**，不是已实现的 Windows 系统通知、声音、微信或邮件推送。组件退出、电脑关机/休眠或网络不可用时无法持续监测；重新打开后再核对，不保证补发所有漏掉的动态。

### 3.2 中国时间与实际周期

- 时间统一显示为北京时间 **UTC+8**。美国时区存在夏令时差异，不能全年固定加 15 或 16 小时。
- 预告时间来自已经提供时间戳/时区的有效窗口，并标注社区估计或期限；未能确定时，不输出伪精确的“中国重置时间”。
- 角色下方的上下结构翻页时钟显示账户实际五小时周期的剩余时间，不是 Tibo 预告倒计时。预告不会提前改写当前账户周期。
- 账户确认新的五小时 `resetsAt` 后，倒计时才切换到新终点；没有新终点时，到零保持零并等待同步，不凭空再加五小时。
- 同一连接临时断网时，已有可靠终点继续每秒本地计时，并提示旧数据/重试。重新建立 CLI 连接后不沿用旧连接的倒计时、重置结论或机会缓存，等待该连接的新鲜账户数据。未返回五小时限制时隐藏对应窗口，不假设 Plus/Pro 永久有相同限制。

### 3.3 怎样确认“已经重置”

**首次读取，以及每次重新建立 CLI 连接后的首份数据，只建立基线；哪怕周额度为 100% 也不宣告“刚完成重置”。** 有有效窗口元数据且先前已消耗的额度恢复到 100% 时，立即确认一次恢复；若错过 100% 的瞬间，还须观测到同类周窗口的终点推进、使用率下降且新周期起点在最近 6 小时内。窗口倒退、周期类型改变、缺失时间或仅有帖子预告都不确认。95% 本身不是重置证据。

同一周期不会因重复刷新一直重播庆祝。周额度回满只能确认账户当前额度状态，不能证明一定由某条 Tibo 帖子造成，也不能代替检查五小时/特定模型限制。重置机会、实际额度和 Credits 是不同字段；未上报不等于零次。

### 3.4 多久检查一次

以下为运行中、请求不被上游限流时的目标间隔，不是实时承诺。帖子轮询增加约正负 15% 的随机错峰，请求耗时也会造成延迟；原帖核验另有短时缓存。

| 状态 | Tibo 动态目标间隔 | 账户额度目标间隔 |
| --- | --- | --- |
| 普通监测，无有效预告 | 约 5 分钟 | 15 秒 |
| 距离预告窗口超过 2 小时 | 约 3 分钟 | 15 秒 |
| 距离窗口在 15 分钟至 2 小时之间 | 约 1 分钟 | 5 秒 |
| 临近 15 分钟内、窗口内或近期等待确认 | 约 20 秒 | 5 秒 |
| 本账户重置已确认 | 约 3 分钟 | 15 秒 |
| 动态失败 | 30、60、120、240、300 秒逐步退避，最多约 5 分钟 | 15 秒 |

过期预告较久后恢复较低频率。面板悬停可查看源更新时间与下一次检查时间；倒计时每秒刷新是本地计算，不是每秒请求 X 或 Codex。重新刷新不能保证第三方接口已有新帖子。

### 3.5 数据来源

| 数据 | 来源和刷新 |
| --- | --- |
| 套餐、用量、重置机会 | 本机 Codex app-server 的只读账户/限额接口，正常轮询并接受通知；异常可重连，恢复后以新数据为准。 |
| 倒计时 | 已知账户时间戳，每秒本地更新，不是每秒发请求。 |
| Tibo 动态 | 公开第三方接口/受约束来源，动态错峰检查与失败退避；不保证每条帖子都能获取。 |
| 中文概要 | 外部机译与本机缓存，可能不准确；悬停/原帖便于核对。 |
| 软件更新 | 开启后访问指定 GitHub 仓库；每 24 小时最多一次尝试，包含失败尝试，重启不绕过间隔。 |

公开动态源为 `codex.gussuriworks.com/api/current`；原帖核验使用 `api.fxtwitter.com` / `api.vxtwitter.com`；中文机译使用 FxEmbed 接口并缓存。本项目不训练自己的重置概率模型，不计算参考项目的 12/24/48/72 小时概率。第三方来源可能延迟、限流、变更或停服。[联网和保存内容](PRIVACY.md)。

刷新失败不等于额度为零；字段缺失显示未知。赠送机会、Tibo 预告、套餐周期自然更新和本账户实际恢复额度是不同状态。

## 4. 自动升级与维护

### 4.1 开关与更新源

右键组件 → “配置本软件的更新仓库…” → 填写本软件所有者的具体 owner/repo → 保存。保存新源后仍保持关闭，确认可信源后再勾选“自动更新”。“查看更新状态”展示当前版本、源与检查状态，不绕过 24 小时限制。

默认无源、不开启，不因安装就联网查更新。本版不接收 GitHub Token，仅支持无凭据可读的公开 Release；私有源码仓库不能直接作为此模式的下载源。仓库已创建也不代表已配置可用更新：还必须公开可读，并上传符合协议的正式 Release 资产。源码提交和预览版 Release 不会触发本版自动更新。

### 4.2 检查、验证、切换

1. 只检查指定仓库的最新正式 Release，跳过草稿、预览版和不高于当前的版本。
2. 只接受名称和版本严格匹配的 Windows x64 ZIP，必须有 GitHub 返回的 SHA-256 digest；缺失即拒绝更新。
3. 核对下载长度、大小限制与摘要，再校验 ZIP 白名单、清单版本和各文件哈希，拒绝额外文件、路径穿越和链接条目。
4. 本机独立更新助手复核文件，等待组件正常退出后替换四个运行文件，启动新版并等待版本就绪；可恢复的失败恢复备份并尝试旧版。

只切换本组件，不重启电脑或正在工作的 Codex。校验或下载失败继续使用当前版本；不会改登录、代理和开机设置。磁盘损坏、权限异常或持续占用可能导致回滚失败，此时保留备份，需要人工恢复，不能承诺任何故障都无感。

GitHub 摘要用于完整性核验，不是独立作者签名，不能抵御发布账号被攻破。发布者仍应保护仓库与 Release 权限。[完整协议和发布步骤](docs/AUTO-UPDATE.md)。

### 4.3 本地状态

运行状态、偏好、CLI 路径设置、更新缓存和公开机译缓存统一保存在 `%LOCALAPPDATA%/CodexUsageWidget`；每个安装目录使用独立哈希命名空间，健康就绪记录不含额度或译文。旧目录的个人状态与倒计时不自动导入，以免串用不同账户；旧文件保留不删除。新启动先读取官方账户数据，单次运行内断网仍继续可靠倒计时。安装目录只有更新运行文件时需要可写；不自动提权。

## 5. 开发与验证

在源码根目录运行：

~~~powershell
dotnet test tests/CodexUsageWidget.Tests/CodexUsageWidget.Tests.csproj -c Release
powershell.exe -NoProfile -File scripts/Test-SourcePackaging.ps1
powershell.exe -NoProfile -File scripts/Test-Launcher.ps1
dotnet publish CodexUsageWidget.csproj -c Release --self-contained false -p:UseAppHost=false -o bin/review-build
powershell.exe -NoProfile -File scripts/Test-Diagnostics.ps1 -PublishPath bin/review-build
~~~

测试使用隔离夹具，不代表真实账户或真实 GitHub Release 已端到端通过。build.ps1 是个人布局构建脚本，不是通用安装器；默认不写桌面，只有明确传入 -InstallDesktop 才调用桌面入口安装器。源码导出使用 scripts/Prepare-Source.ps1，白名单排除运行状态、凭据、旧素材、运行二进制和个人 README。图片采用逐文件、固定哈希白名单；只包括原创图标、已授权真实截图与本次明确标注模拟数据的中英文界面预览，不放开任意 PNG。

## 6. 安全与隐私

- 只启动/清理组件自己创建的 app-server，不终止其他 Codex 或编辑器进程。
- 不读取或复制 auth.json，不修改 Codex 模型配置，不接收 OpenAI/GitHub Token。
- 外部快报和机译是联网功能，不应视为官方信息；更新请求不携带账户额度、帖子内容或对话。
- 不承诺绝对无侵权、所有平台兼容或商用认证；原创设计、完整代码许可、发行签名和依赖审计仍分别核查。

[隐私与联网](PRIVACY.md) · [安全](SECURITY.md) · [权利](RIGHTS.md) · [第三方](THIRD_PARTY.md) · [审查记录](docs/PUBLISHING.md)

## 7. 常见问题

**一定要“魔法”吗？** 需要能访问上述服务的网络；能直连时不必额外开代理，无法直连时通常需要自己的可用代理。组件不提供节点，也不能保证付费机场或某个节点稳定。

**能预测“充值时间”吗？** 不能。本项目解释重置预告和赠送重置机会，不预测付款、充值到账、购买 Credits 的时间，不自动充值或使用重置卡。

**为什么发文后没有马上提醒？** 不是 X 官方实时订阅，第三方收录、核验缓存、网络、错峰轮询与失败退避都会延迟；原帖按钮用于自行核对。

**为什么 Plus/Pro 显示 --？** 本次接口没有返回 planType，或当前 CLI 入口未成功连接；不会用旧缓存猜套餐。

**为什么剩余很多但 Codex 不能用？** 模型独立限制、登录、代理、服务状态或缓存都可能影响可用性；百分比不是可用性保证。

**为什么自动更新没有请求？** 检查是否已开启、具体更新仓库是否配置、是否还在 24 小时间隔、仓库是否有公开正式 Release、资产是否符合协议。主页和 Projects 页面都不是更新仓库。

**私有仓库怎么给用户升级？** 本版不处理私有仓库认证。可以另建经过审查的公开二进制发行仓库，或暂时由维护者手动提供校验后的安装包；不要向用户分发发布 Token。

**网络失败会丢掉倒计时吗？** 已有可靠周期继续本地倒计时，并显示刷新异常；没有可靠时间戳时显示等待同步，不伪造五小时。

## 设计参考

说明章节与“正式 Release + 摘要校验”思路参考 [Codex Desktop Usage Monitor Windows](https://github.com/JiaYang-BUAA/Codex-Desktop-Usage-Monitor-Windows)（仓库标注 MIT）；本组件独立实现 WPF 桌面功能，没有复制它的 CDP/Node 后台实现，也没有把对方功能或许可证套用到本项目。
