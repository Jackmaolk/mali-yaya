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

2026.10.28.5 起，Tibo 联网监测首次使用默认开启，启动后自动查询公开动态、核对帖文并按需机译；无需登录 X。用户可在右键菜单独立关闭，升级保留已经保存的关闭选择。缺失设置文件采用首次使用默认值；损坏或不安全的设置仍停止联网。关闭会取消动态/机译请求及重试、拒绝迟到结果，不影响账户额度与本地倒计时。已发送请求无法撤回；极简模式和折叠不是联网开关。退出组件停止全部轮询及其自行启动的 Codex 子进程。

## 本地保存

- `%LOCALAPPDATA%/CodexUsageWidget/runtime/<安装目录哈希>/widget-runtime.json`：仅版本、程序集标识、PID、运行状态及更新时间，用于启动与更新就绪判断。
- 同目录的 `account-runtime.json`：额度、周期终点、倒计时锚点及快报/译文。**包含个人使用情况，不应上传。** 不恢复到新启动/不同账户。
- `%LOCALAPPDATA%/CodexUsageWidget/cache/tibo-chinese-cache.json`：已核实公开帖文的机译缓存；Tibo 关闭时不初始化机译客户端。
- LocalAppData 的 `restart-widget.log`：仅版本、程序集编号、PID 和操作状态；历史项目目录日志不自动读取、删除或导入。
- `%LOCALAPPDATA%/CodexUsageWidget/preferences.json`：Tibo 联网与极简模式两个独立开关。
- 勾选开机启动时，写入当前用户注册表 `Software\Microsoft\Windows\CurrentVersion\Run` 的 `CodexUsageWidget` 项；取消勾选会移除该项。
- `%LOCALAPPDATA%/CodexUsageWidget/update-settings.json`：更新开关与明确选择的仓库名。
- `%LOCALAPPDATA%/CodexUsageWidget/updates`：持久化检查间隔、下载/解包暂存、更新助手与旧运行文件备份；不包含 Codex 登录文件。更新失败仍保留 24 小时冷却，不自动反复请求。

新状态目录是当前 Windows 用户的 LocalAppData，安装目录只保存运行程序。为避免跨账户串用，旧私人状态不自动导入，倒计时新启动需重新取得官方数据；同一连接内临时断网保留已确认终点；新 CLI 连接不沿用旧连接的未绑定倒计时、重置结论或机会数据。仅使用不序列化的随机内存连接标记，不读取账号 ID 或登录凭据。LocalAppData 并非 Codex 账户级隔离；本机恶意同用户进程不在绝对保护承诺内。拒绝网络路径及可检测的重解析点，不自动提权或回退到程序目录。新脚本不会清理历史文件，以免删除个人数据。发布白名单及 `.gitignore` 会排除这些状态、缓存、日志、备份和凭据。

## 删除

先关闭自动更新、取消开机启动，再退出组件，然后可删除组件安装目录中的状态、缓存、日志及上述本组件 LocalAppData 目录。确认没有更新助手运行后再删除暂存和备份。不要删除 Codex 的登录或配置来清理本组件。删除本组件不代表删除任何第三方服务器记录。
