# 自动更新协议与发行步骤

## 默认边界

默认关闭、仓库为空。仅接受明确配置的 Jackmaolk/具体仓库；不会连接参考项目或根据个人主页猜测仓库。2026-10-28 已通过连接核实 Jackmaolk/mali-yaya 存在且当前连接具有仓库写权限，但创建时仍为 Private；所有者现已要求公开，公开状态与实际上传结果需要另行验证。尚未发布真实 Release。源码上传与公开二进制发行是不同事项，本版不保存 GitHub Token。

## 发布协议

版本使用 YYYY.MM.DD.revision，例如 2026.10.28.3；正式 tag 必须为 v2026.10.28.3，ZIP 必须为 mali-yaya-2026.10.28.3-windows-x64.zip。不接受 prerelease 或 draft。版本须高于正在运行的版本，比较数字而非字符串。

资产必须在 GitHub Release API 中提供 sha256: 后接 64 位十六进制 digest；缺失、名称不符、重复资产、非法跳转、长度不符或哈希不符都拒绝，不使用旁边随意提供的文本文件代替 GitHub 摘要。

ZIP 只含以下根级文件，禁止子目录、链接和额外内容：

- CodexUsageWidget.dll
- CodexUsageWidget.exe
- CodexUsageWidget.deps.json
- CodexUsageWidget.runtimeconfig.json
- release-manifest.json

清单结构为 product=mali-yaya、version=目标版本、platform=win-x64、runtimeMajor=9，files 为上述四个运行文件名到 SHA256 字符串的对象。源码 ZIP、PDB、截图、状态、旧素材和脚本不能用作自动更新包。

## 生成运行包

先从已验证源码发布框架依赖 DLL，并用现有 launcher/WidgetLauncher.cs 编译 Windows x64 WinExe 启动器到同一输出目录。然后运行：

~~~powershell
powershell.exe -NoProfile -File scripts/Prepare-Release.ps1 -PublishPath bin/review-build -OutputDirectory bin/mali-yaya-2026.10.28.2-windows-x64 -Version 2026.10.28.2
~~~

脚本只复制白名单四个运行文件，使用无窗口/无网络的 --diagnose-build 检查构建标识与版本，再生成清单和 ZIP。不要把示例版本号当作下一次发行版本；修改 WidgetRuntimeStatus.BuildVersion、测试并重新构建后使用对应版本。

经用户明确批准发布正式 GitHub Release、上传严格匹配的 ZIP，并核对 GitHub API 给出的摘要；本地打包脚本不创建仓库、不发布、不上传。

## 运行策略

启用且配置正确时，运行中的组件每 15 分钟唤醒调度器，真正的 GitHub 请求仍受持久化 24 小时冷却限制。开始尝试前保存时间，失败、取消、重启或更换仓库不能缩短间隔。关闭更新或组件后停止后续请求。冷却状态损坏时安全降级，不立即反复请求。

下载和解包有大小及时间边界，先校验 GitHub ZIP 摘要再解包。助手从本机当前程序集复制到独立目录，重新检查暂存文件和旧文件摘要，等父组件正常退出后替换；不强制结束父组件，不重启其他 Codex。程序目录不可写、文件被占用或版本无法就绪时，尝试回滚；无法回滚时保留备份，不递归删除用户目录。替换采用目标目录临时文件、落盘后原子重命名，DLL 最后切换；更新期间保留持久化恢复日志，正常启动遇到未完成日志会校验备份并恢复旧运行文件。排他锁覆盖恢复过程，避免两个助手同时写入。

下载哈希保证的是与 GitHub 元数据一致，不是作者签名或独立供应链认证。正式商用仍需受支持运行库、代码签名、账号安全与干净系统验收。

## 依据

- [GitHub Release 资产 API：digest 与下载](https://docs.github.com/en/rest/releases/assets)
- [GitHub Release API](https://docs.github.com/en/rest/releases/releases)
- [参考项目及其说明](https://github.com/JiaYang-BUAA/Codex-Desktop-Usage-Monitor-Windows)

未复制参考项目代码。本轮测试采用离线 HTTP/ZIP 夹具及文件事务测试；没有真实仓库/Release 时，不宣称联网热更新验收通过。
