# CLI 自动识别与连接修复（2026.10.28.1）

## 使用方法

正常打开组件即可自动识别。右键小组件可选择：

- **重新自动识别 Codex CLI**：移除本组件手动保存的路径，重新检查安装位置；不改全局 PATH、代理、Codex 配置或登录。
- **手动选择 Codex CLI**：选可信的原生 `codex.exe`，不是快捷方式、`.cmd`、`.ps1` 或 Desktop 图形主程序。
- **查看 Codex 连接信息**：查看来源、版本和完整路径。底部状态悬停也可查看；分享诊断时注意隐藏个人路径。

切换入口时会清空旧额度、赠送次数和旧倒计时，再读取当前 CLI 账户；不会复制账户、读取登录令牌文件或自动登录。

## 检测顺序

1. 用户手动保存的路径，或专用 `CODEX_WIDGET_CODEX_PATH` 环境变量。显式指定失效会提示修正，不偷偷切换。
2. Desktop 提供的 `CODEX_CLI_PATH` 提示。它可能指向已删除的旧版本；无效时继续检测，不因此卡住。
3. 当前进程与用户/系统 PATH 中的原生 `codex.exe`；npm 前缀下已知的原生 payload，不执行 shell shim。
4. 独立安装版、Desktop 已知目录、npm 全局目录以及可访问的 Store 安装目录。
5. 当前用户 VSCode、VSCode Insiders、Cursor、Windsurf 的已知 OpenAI 扩展目录。

每次连接重建都会重新检查环境，因此安装 CLI、更新安装目录或修改用户 PATH 后无需重启电脑。健康的现有连接不会为了新版本突然中断。自动发现并不保证覆盖所有自定义安装位置；找不到时可手选。

只检查这些明确位置的有限层级，不扫描全盘、不修改权限、不申请管理员权限，不探测网络共享或执行当前目录的可疑脚本。读取 PE 头筛选控制台 EXE 和可用架构，再以 `--version` 核对 `codex-cli` 输出；这不是数字签名或恶意代码认证，仍需选择可信来源。

每个版本探测最长约 2 秒，一轮最多 8 个候选，目录扫描等待和版本探测共用约 12 秒预算。目录扫描在后台执行，切换入口或关闭时取消等待与循环检查；无法保证中断已进入内核的阻塞文件操作。账户握手及限额请求另有 12 秒超时。

仅在自动模式遇到明确的初始化协议不兼容（未知初始化方法、参数，或不支持 app-server/--listen）时，才结束该自建子进程并尝试后备位置。登录失效、账户类型、权限和网络错误不会触发偷偷切换其他入口。手动指定路径始终不回退。

## 连接过程

通过 stdio 启动本机 app-server，依次发送 `initialize`、`initialized`、只读 `account/read`，再查询限额。初始化失败会结束本组件自己创建的子进程，下一次可以重新连接，不把未初始化的进程当作健康连接。

一次用量超时先重试；连续两次超时、输出流关闭、无效 JSON 或缺失用量主体会清理本组件自建连接，下轮重新识别并连接。保留上次成功数据并标明刷新失败，不用旧数据伪装成刚读取成功。不会结束其他 Codex 或编辑器进程。

未登录时提示运行 `codex login`；API Key 模式提示切换至支持套餐额度的账户；协议不兼容提示升级或选择其他已安装版本；数据目录权限错误与网络超时分开提示。组件不会自动运行登录、安装、更新或修改凭据。

账户原始返回和 stderr 不写入诊断文件；状态只使用脱敏后的固定提示。不会因为有余额就保证任何模型必然可用。

## 路径保存

只保存手动选择的 EXE 路径和结构版本，位于 `%LOCALAPPDATA%\CodexUsageWidget\connection-settings.json`。自动识别不需要写设置文件。损坏或过大的设置会退回自动模式；手动路径不存在时会提示重新选择。文件不含账户、令牌或密码，不应进入源码包。

## 本地诊断

```powershell
dotnet bin/Release/net9.0-windows/CodexUsageWidget.dll --diagnose-cli
dotnet bin/Release/net9.0-windows/CodexUsageWidget.dll --diagnose-connection
powershell.exe -NoProfile -File scripts/Test-Launcher.ps1
```

第一条仅检测路径和版本。第二条启动只读连接并验证是否收到限额，只输出连接结果，不输出具体账户用量；它使用当前用户自己的 Codex 环境，可能需要网络且可能因权限而失败。两者不打开小组件界面，不干扰正在运行的组件。

启动器还支持 `CodexUsageWidget.exe --diagnose`：仅检查 DLL 和 dotnet 宿主路径，不启动真实组件。分发时应完整解压文件夹，启动器优先使用相邻 DLL；旧桌面复制启动器仍保留兼容路径。当前仍需安装匹配的 .NET Desktop Runtime，并非自包含安装包。

## 依据与范围

检测布局基于本机实际安装和官方 npm 启动器的已知 payload 规则；没有复制第三方检测器代码、引入新包或下载 CLI。

- [OpenAI Codex 源码](https://github.com/openai/codex) 与其 Apache-2.0 许可。
- [Windows 部署说明](https://learn.chatgpt.com/docs/enterprise/windows-deployment)：独立 CLI 的用户安装与 PATH 行为。
- [app-server 协议](https://developers.openai.com/codex/app-server)：初始化和账户读取。

这些验证不代表所有 Windows、ARM64 设备或所有历史 CLI 版本均已实际测试。当前发行目标仍是 Windows x64。
