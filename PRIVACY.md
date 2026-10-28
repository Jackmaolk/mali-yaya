# 隐私与联网说明

## 账户读取

组件启动本机已安装的 Codex `app-server --listen stdio://`，发送初始化和 `account/rateLimits/read` 请求，并接收用量通知。Codex 使用你现有的登录与配置访问其服务。组件不要求输入新的 API 密钥，不提供自动消费重置机会、付款或代登录功能。

2026.10.27.4 起会先通过只读 `account/read` 检查登录模式，丢弃返回中的账号细节，不将其写入状态或日志；不会读取并复制 `auth.json`。手动选择的 CLI 路径单独保存在当前用户 LocalAppData 的 `CodexUsageWidget/connection-settings.json`，不含凭据。

现有源码没有把账户额度、对话或登录凭据发送给 Tibo 动态/翻译接口的代码路径。这是源码审查结果，不是对 Codex 本身或第三方服务行为的保证。

## 外部接口

- `codex.gussuriworks.com/api/current`：社区公开动态与预估窗口。
- `api.fxtwitter.com`、`api.vxtwitter.com`：公开帖子核对；FxEmbed 同时提供公开帖文的中文机译。
- `x.com/thsottiaux/status/...`：用户主动点击时交给默认浏览器打开。
- 自动更新默认关闭且无源。明确开启后，仅请求配置的本软件 GitHub 仓库的 Release 元数据和严格匹配的 ZIP；允许的 GitHub 资产下载跳转见更新协议。不发送账户额度、对话、Tibo 内容或登录凭据，不保存 GitHub Token。

社区和镜像接口不是 OpenAI 官方账户数据源；它们能看到请求来源 IP、时间、公开帖子 ID 等通常的网络信息。系统代理可能影响访问。不要把接口内容当作可执行指令，机译也不能作为账户已经重置的证据。

当前组件没有独立的关闭动态监测开关；退出组件会停止全部轮询及其自行启动的 Codex 子进程。后续公开发行前建议增加联网功能的独立开关。

## 本地保存

- 程序目录的 `widget-runtime.json`：进程、版本、用量/重置时间、倒计时锚点及公开动态状态。**包含个人使用情况，不应上传。**
- 程序目录的 `tibo-chinese-cache.json`：已核实公开帖文的机译缓存。
- 项目目录的 `restart-widget.log`：重启诊断；新版脚本仅写版本、程序集编号、PID 和操作状态，旧文件可能仍含过去的账户用量。
- 勾选开机启动时，写入当前用户注册表 `Software\Microsoft\Windows\CurrentVersion\Run` 的 `CodexUsageWidget` 项；取消勾选会移除该项。
- `%LOCALAPPDATA%/CodexUsageWidget/update-settings.json`：更新开关与明确选择的仓库名。
- `%LOCALAPPDATA%/CodexUsageWidget/updates`：持久化检查间隔、下载/解包暂存、更新助手与旧运行文件备份；不包含 Codex 登录文件。更新失败仍保留 24 小时冷却，不自动反复请求。

当前状态目录随安装路径保存，尚未迁移至 LocalAppData；因此不适合直接安装到只读目录。新脚本不会清理历史文件，以免删除你的个人数据。发布白名单及 `.gitignore` 会排除这些状态、缓存、日志、备份和凭据。

## 删除

先关闭自动更新、取消开机启动，再退出组件，然后可删除组件安装目录中的状态、缓存、日志及上述本组件 LocalAppData 目录。确认没有更新助手运行后再删除暂存和备份。不要删除 Codex 的登录或配置来清理本组件。删除本组件不代表删除任何第三方服务器记录。
