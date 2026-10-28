using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CodexUsageWidget;

// A window captures its UI language once. This never changes thread/OS culture or domain data.
public sealed class UiText
{
    public UiText(CultureInfo? culture = null)
    {
        IsChinese = (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName
            .Equals("zh", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsChinese { get; }

    public static IReadOnlyDictionary<string, string> EnglishBySource { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["码力芽芽"] = "Mali Yaya",
            ["等待同步"] = "Syncing",
            ["等待更新检查"] = "Waiting for update check",
            ["自动更新已关闭"] = "Automatic updates off",
            ["更新已校验，准备切换组件…"] = "Update verified; preparing to switch...",
            ["更新未能切换，继续使用当前版本"] = "Could not apply update; using current version",
            ["更新未能完成，继续使用当前版本"] = "Update incomplete; using current version",
            ["请先配置本软件的具体更新仓库。当前没有默认更新源，不会下载参考项目的程序。"] = "Configure this widget's update repository first. No default source is configured; reference projects are never downloaded.",
            ["自动更新"] = "Automatic updates",
            ["无法保存更新设置，未改变原设置。"] = "Cannot save update settings; original settings unchanged.",
            ["配置码力芽芽更新仓库"] = "Configure Mali Yaya update repository",
            ["填写具体仓库（owner/repo），不要填写 Token。"] = "Enter a repository (owner/repo), not a token.",
            ["例如 Jackmaolk/mali-yaya；建议名不代表仓库已创建。需要公开 Release，无凭据访问。"] = "Example: Jackmaolk/mali-yaya. This suggested name does not mean the repository exists. Public releases and credential-free access are required.",
            ["保存并保持自动更新关闭"] = "Save with automatic updates off",
            ["请填写 Jackmaolk/具体仓库名，不能是个人主页或其他软件仓库。"] = "Enter Jackmaolk/repository-name, not a profile or another app's repository.",
            ["仓库格式不正确"] = "Invalid repository",
            ["更新仓库已保存，自动更新已关闭"] = "Repository saved; automatic updates off",
            ["无法保存更新设置。"] = "Cannot save update settings.",
            ["当前版本：{0}\n更新仓库：{1}\n"] = "Current version: {0}\nUpdate repository: {1}\n",
            ["未配置"] = "Not configured",
            ["自动更新：{0}\n{1}\n"] = "Automatic updates: {0}\n{1}\n",
            ["开启"] = "On",
            ["关闭"] = "Close",
            ["开启后每 24 小时最多尝试一次；失败不绕过间隔。私有仓库不支持无凭据自动更新。\n"] = "When enabled, at most one attempt is made every 24 hours, including failures. Private repositories cannot be updated without credentials.\n",
            ["只切换本组件，不重启 Codex。"] = "Only this widget is replaced; Codex is not restarted.",
            ["码力芽芽更新状态"] = "Mali Yaya update status",
            ["正在安排"] = "Scheduling",
            ["动态查询波动，自动退避重试"] = "Feed unavailable; retrying with backoff",
            ["自动错峰监测"] = "Staggered automatic monitoring",
            ["{0}\n原文：{1}\n"] = "{0}\nOriginal: {1}\n",
            ["中文机译：{0}\n"] = "Chinese machine translation: {0}\n",
            ["源更新（北京时间）：{0:M月d日 HH:mm:ss}\n"] = "Source checked (Beijing UTC+8): {0:MM/dd HH:mm:ss}\n",
            ["{0} · 下次检查：{1}\n"] = "{0} · Next check: {1}\n",
            ["账户刷新：{0:0} 秒"] = "Account refresh: {0:0}s",
            ["\n点击打开 Tibo 原帖"] = "\nOpen Tibo's original post",
            ["原文："] = "Original: ",
            ["{0}\n点击查看本条原帖"] = "{0}\nOpen this original post",
            ["中文：正在获取译文…"] = "Chinese: fetching translation...",
            ["中文机译节选，完整译文获取后可悬停查看"] = "Chinese machine translation excerpt; hover for the full translation when available",
            ["中文：暂时无法获取，稍后自动重试"] = "Chinese: unavailable; retrying later",
            ["中文："] = "Chinese: ",
            ["原文仍可查看；翻译失败不影响额度和重置监测"] = "Original remains available; translation failure does not affect quota or reset monitoring",
            ["中文机译全文（{0}）：\n{1}\n仅供阅读，重置判断以原文与账户数据为准"] = "Full Chinese machine translation ({0}):\n{1}\nFor reading only; reset verification uses the original and account data",
            ["无限"] = "Unlimited",
            ["可用"] = "Available",
            ["无"] = "None",
            ["已用 --%"] = "Used --%",
            ["等待数据"] = "No data",
            ["已用 {0}%"] = "Used {0}%",
            ["芽芽智能体 · {0}"] = "Yaya · {0}",
            ["重置完成！"] = "Reset!",
            ["期待重置"] = "Waiting",
            ["机会在手"] = "Bonus",
            ["精神满满"] = "Ready",
            ["轻松工作"] = "Relaxed",
            ["认真专注"] = "Focused",
            ["有点担心"] = "Careful",
            ["额度告急"] = "Low quota",
            ["码力芽芽还在读取用量"] = "Mali Yaya is reading usage",
            ["未知"] = "Unknown",
            ["太好了，有 {0} 次重置机会！"] = "Great! {0} resets available!",
            ["每周剩余 {0} · {1}"] = "Weekly left {0} · {1}",
            ["到时待确认"] = "Verifying",
            ["继续计时"] = "Counting",
            ["5小时重置"] = "5h reset",
            ["（北京时间）"] = " (Beijing UTC+8)",
            ["\n依据上次确认的时间继续计时，正在重试同步账户"] = "\nCounting from the last confirmed time; retrying account sync",
            ["\nTibo 已预告重置；账户实际上报新时间后才切换周期"] = "\nTibo announced a reset; the cycle changes only when the account reports a new time",
            ["\n已到原定时间，保持零，等待账户确认下一轮时间"] = "\nScheduled time reached; holding at zero until the account confirms the next cycle",
            ["重置时间等待上报"] = "Reset pending",
            ["即将刷新"] = "Due now",
            ["{0}天{1}时"] = "{0}d {1}h",
            ["{0}时{1}分"] = "{0}h {1}m",
            ["{0:M月d日 HH:mm}重置 · {1}{2}"] = "Resets {0:MM/dd HH:mm} · {1}{2}",
            ["后"] = " left",
            ["协议未上报"] = "Not reported",
            ["暂无可用重置"] = "None available",
            ["可用 {0} 次"] = "{0} available",
            ["可用 {0} 次 · {1:M月d日}前"] = "{0} until {1:MM/dd}",
            ["已检测 {0} · {1:HH:mm:ss}"] = "Detected {0} · {1:HH:mm:ss}",
            ["账户每{0:0}秒刷新：{1}\n悬停芽芽快报可查看下次动态检查时间"] = "Account refresh every {0:0}s: {1}\nHover over Yaya News for the next feed check",
            ["\n运行版本：{0}"] = "\nBuild: {0}",
            ["CLI 来源：{0}\n版本：{1}\n路径：{2}\n右键可重新识别或选择 CLI；使用本机现有登录，不复制凭据。"] = "CLI source: {0}\nVersion: {1}\nPath: {2}\nRight-click to detect or select CLI. Uses your existing local sign-in; no credentials are copied.",
            ["尚未识别可用 CLI；右键小组件可重新识别或手动选择 codex.exe。"] = "No usable CLI detected. Right-click the widget to detect or select codex.exe.",
            ["Codex 连接信息"] = "Codex connection info",
            ["正在检测连接，请稍候再选择 CLI。"] = "Checking connection; please wait before selecting a CLI.",
            ["选择可信的 Codex CLI 原生程序（不是桌面快捷方式或脚本）"] = "Select a trusted native Codex CLI executable (not a shortcut or script)",
            ["请选择与当前 Windows 架构兼容的 Codex CLI 原生 EXE。"] = "Select a native Codex CLI EXE compatible with this Windows architecture.",
            ["CLI 设置未完成；请查看错误提示。"] = "CLI setup incomplete; see error details.",
            ["Codex 连接设置"] = "Codex connection settings",
            ["等待账户"] = "Waiting",
            ["芽芽智能体 · 重新连接中"] = "Yaya · Reconnecting",
            ["等待读取当前 CLI 的账户用量，不沿用旧数据。"] = "Reading this CLI's account usage; old data is not reused.",
            ["等待连接"] = "Offline",
            ["开机启动设置失败：{0}"] = "Startup setting failed: {0}",
            ["重新自动识别 Codex CLI"] = "Detect Codex CLI again",
            ["手动选择 Codex CLI…"] = "Select Codex CLI...",
            ["查看 Codex 连接信息"] = "View Codex connection info",
            ["自动更新（每 24 小时检查）"] = "Automatic updates (every 24 hours)",
            ["配置本软件的更新仓库…"] = "Configure update repository...",
            ["查看更新状态"] = "View update status",
            ["立即刷新；右键小组件可自动识别或手动选择 CLI"] = "Refresh now; right-click the widget to detect or select CLI",
            ["折叠/展开"] = "Collapse/expand",
            ["原创叶片机器人芽芽"] = "Yaya, an original leaf robot",
            ["芽芽：原创叶片机器人，点击可互动"] = "Yaya: an original leaf robot. Click to interact.",
            ["状态轻松"] = "Relaxed",
            ["时"] = "h",
            ["分"] = "m",
            ["秒"] = "s",
            ["5小时使用限额"] = "5h limit",
            ["每周使用限额"] = "Weekly",
            ["暂未检测到使用限额"] = "No usage limits detected",
            ["芽芽智能体会继续检查"] = "Yaya will keep checking",
            ["芽芽智能体 · 正在分析"] = "Yaya · Analyzing",
            ["读取额度后给出建议"] = "Advice after quota sync",
            ["随机重置"] = "Resets",
            ["等待官方上报"] = "Pending",
            ["芽芽快报 · Tibo"] = "Yaya News · Tibo",
            ["查看原帖 ↗"] = "Source ↗",
            ["Tibo 动态 · 正在检查"] = "Tibo · Checking",
            ["正在获取动态与本账户状态"] = "Fetching posts and account status",
            ["正在连接…"] = "Connecting...",
            ["开机启动"] = "Start at sign-in",
            ["暂无限额"] = "No limit",
            ["放心使用"] = "Go ahead",
            ["节奏正常"] = "On track",
            ["留意消耗"] = "Watch use",
            ["谨慎使用"] = "Use care",
            ["短时额度紧张"] = "Low quota",
            ["等待短时重置"] = "Reset due",
            ["周余量充足"] = "Plenty left",
            ["周节奏正常"] = "On track",
            ["规划后续用量"] = "Plan ahead",
            ["保留关键任务"] = "Key tasks",
            ["周额度紧张"] = "Low quota",
            ["等待周重置"] = "Reset due",
            ["太好了 · 可重置1次"] = "Great! 1 reset available",
            ["发现随机重置"] = "Bonus resets",
            ["等待周额度"] = "Awaiting weekly",
            ["Codex 暂未返回每周使用限额，保留上次数据。"] = "Codex has not returned weekly limits; keeping the last reading.",
            ["优先关键任务"] = "Key tasks first",
            ["消耗速度偏快"] = "Fast pace",
            ["节奏健康"] = "Healthy pace",
            ["按计划使用"] = "On track",
            ["本账户重置已完成"] = "Account reset",
            ["周额度已回满！芽芽继续关注 Tibo 新动态。"] = "Weekly quota restored! Yaya is watching Tibo for news.",
            ["等待预告重置"] = "Reset pending",
            ["Tibo 已预告，但额度未确认回满；芽芽持续核对。"] = "Tibo announced a reset; Yaya is checking for account recovery.",
            ["核对额度中"] = "Checking quota",
            ["公开动态报告重置，正在等待本账户额度回满证据。"] = "A public post reports a reset; waiting for account recovery evidence.",
            ["即将重置"] = "Reset due",
            ["正在自动识别 Codex CLI…"] = "Detecting Codex CLI...",
            ["Codex 已连接"] = "Codex connected",
            ["Codex 未返回用量数据。"] = "Codex returned no usage data.",
            ["Codex 用量请求超时。"] = "Codex usage request timed out.",
            ["刷新波动，正在自动重试…"] = "Refresh interrupted; retrying...",
            ["暂时无法刷新，保留上次数据，请稍后手动刷新"] = "Cannot refresh; keeping last data. Refresh manually later.",
            ["暂时无法刷新，保留上次数据，下轮自动重试"] = "Cannot refresh; keeping last data. Retrying next cycle.",
            ["无法启动 Codex app-server。 "] = "Cannot start Codex app-server.",
            ["Codex 请求失败"] = "Codex request failed",
            ["Codex CLI 尚未登录；请先在终端运行 codex login，再点击刷新。"] = "Codex CLI is not signed in. Run codex login in a terminal, then refresh.",
            ["Codex CLI 的账户协议不兼容，请升级 CLI 后重试。"] = "Codex CLI account protocol is incompatible. Upgrade the CLI and retry.",
            ["当前 CLI 使用 API Key；请使用具有 Codex 套餐额度的 ChatGPT 账户登录。"] = "This CLI uses an API key. Sign in with a ChatGPT account that has Codex plan quota.",
            ["当前 Codex CLI 不支持 app-server 协议，请升级 CLI 或手动选择其他已安装版本。"] = "This Codex CLI does not support app-server. Upgrade it or select another installed version.",
            ["当前 Codex CLI 协议不兼容，请升级 CLI 后刷新。"] = "Codex CLI protocol is incompatible. Upgrade it, then refresh.",
            ["Codex 登录已失效；请在终端运行 codex login 后刷新。"] = "Codex sign-in expired. Run codex login in a terminal, then refresh.",
            ["Codex 未返回可用限额；请检查账户登录、套餐支持及代理网络。"] = "Codex returned no usable limits. Check sign-in, plan support, and proxy/network settings.",
            ["Codex 连接超时，请检查代理网络后刷新。"] = "Codex connection timed out. Check proxy/network settings, then refresh.",
            ["无法启动 Codex CLI，请检查文件权限或手动选择可访问的 codex.exe。"] = "Cannot start Codex CLI. Check file permissions or select an accessible codex.exe.",
            ["Codex CLI 连接中断，下次刷新将重新自动识别。"] = "Codex CLI disconnected; the next refresh will detect it again.",
            ["暂时无法连接 Codex；可右键刷新按钮重新识别或手动选择 CLI。"] = "Cannot connect to Codex. Right-click to detect or select CLI.",
            ["Codex 无法初始化本机数据目录；请检查当前用户对 CODEX_HOME 的访问权限，无需复制或修改登录文件。"] = "Codex cannot initialize its local data directory. Check access to CODEX_HOME; do not copy or edit sign-in files.",
            ["环境变量指定"] = "Environment override",
            ["手动指定"] = "Manual selection",
            ["Desktop 提供的 CLI"] = "Desktop-provided CLI",
            ["PATH 中的 CLI"] = "CLI in PATH",
            ["PATH 中的 npm CLI"] = "npm CLI in PATH",
            ["独立安装版 CLI"] = "Standalone CLI",
            ["Desktop 自带 CLI"] = "Desktop bundled CLI",
            ["npm 全局 CLI"] = "Global npm CLI",
            ["系统 npm CLI"] = "System npm CLI",
            ["Store Desktop 自带 CLI"] = "Store Desktop bundled CLI",
            ["IDE 扩展自带 CLI"] = "IDE extension CLI",
            ["无法访问已发现的 Codex CLI，请检查文件权限或手动选择可访问的 codex.exe。"] = "Cannot access the detected Codex CLI. Check permissions or select an accessible codex.exe.",
            ["指定的 Codex CLI 不可用；请右键刷新按钮，重新选择 codex.exe 或恢复自动识别。"] = "Selected Codex CLI is unavailable. Right-click to select codex.exe or restore automatic detection.",
            ["未找到可用 Codex CLI；请安装 CLI，或右键刷新按钮手动选择 codex.exe。支持 npm、独立版及可访问的 Desktop/IDE 安装。"] = "No usable Codex CLI found. Install the CLI or right-click to select codex.exe. npm, standalone, and accessible Desktop/IDE installations are supported.",
            ["请选择本机 Codex 的原生 .exe 文件，不能填写脚本、网络路径或命令参数。"] = "Select a local native Codex .exe, not a script, network path, or command arguments.",
            ["请选择本软件所有者的具体仓库。"] = "Select a repository owned by this app's owner.",
            ["请配置自己的 GitHub 更新仓库"] = "Configure your GitHub update repository",
            ["正在检查更新"] = "Checking for updates",
            ["更新检查冷却中（24 小时）"] = "Update check cooling down (24 hours)",
            ["更新记录不可用，已停止检查"] = "Update record unavailable; checks stopped",
            ["更新检查或校验失败，旧版本未改动"] = "Update check or verification failed; current version unchanged",
            ["更新检查已取消，冷却仍然有效"] = "Update check canceled; cooldown still applies",
            ["暂无可用的新版本"] = "No newer version available",
            ["更新已校验并暂存，等待应用"] = "Update verified and staged; awaiting install",
            ["更新检查正在进行"] = "Update check in progress",
            ["动态来源超过 2 小时未更新"] = "Feed has not updated for over 2 hours",
            ["原帖暂时无法核实，自动重试中"] = "Original post cannot be verified; retrying",
            ["暂无可核实的 Tibo 动态"] = "No verified Tibo posts yet",
            ["继续自动检查公开动态"] = "Continuing automatic public-post checks",
            ["原帖镜像拒绝访问，稍后重试"] = "Post mirror denied access; retrying later",
            ["动态来源连接失败，稍后重试"] = "Feed connection failed; retrying later",
            ["动态来源格式变化，稍后重试"] = "Feed format changed; retrying later",
            ["Tibo 提到赠送重置机会"] = "Tibo mentioned bonus resets",
            ["是否到账请看下方实际重置次数 · 点此看原帖"] = "Check actual reset credits below · Open source",
            ["社区观察到重置 · 核对账户中"] = "Reset reported · Checking account",
            ["公开动态已更新，正在核对本账户实际额度"] = "Public post updated; checking this account's quota",
            ["预告参考时间已过，待确认"] = "Announced time passed; unconfirmed",
            ["请以账户实际额度为准 · 点此看原帖"] = "Use actual account quota · Open source",
            ["北京时间待确认"] = "Beijing time unconfirmed",
            ["Tibo 预告重置"] = "Tibo announced a reset",
            ["Tibo 最近已核实动态"] = "Tibo recent verified post",
            ["Tibo 最新已核实动态"] = "Tibo latest verified post",
            ["原帖暂无文字内容，点击查看"] = "No post text; open source",
            ["Tibo 动态暂不可确认"] = "Tibo status unverified",
            ["本账户重置已完成 · 庆祝！"] = "Account reset confirmed!",
            ["已确认周额度回满，叮当继续监测 Tibo 新动态"] = "Weekly quota restored; Yaya is watching Tibo for news",
            ["点击查看原帖"] = "Open original post",
            ["5小时限额"] = "5h",
            ["每周限额"] = "weekly",
            ["暂无限额窗口"] = "no limits",
            ["重置未上报"] = "resets unreported",
        });

    internal static IReadOnlyDictionary<string, string> XamlSources { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["UiText000"] = "码力芽芽",
            ["UiText001"] = "重新自动识别 Codex CLI",
            ["UiText002"] = "手动选择 Codex CLI…",
            ["UiText003"] = "查看 Codex 连接信息",
            ["UiText004"] = "自动更新（每 24 小时检查）",
            ["UiText005"] = "配置本软件的更新仓库…",
            ["UiText006"] = "查看更新状态",
            ["UiText007"] = "自动更新已关闭",
            ["UiText008"] = "立即刷新；右键小组件可自动识别或手动选择 CLI",
            ["UiText009"] = "折叠/展开",
            ["UiText010"] = "关闭",
            ["UiText011"] = "原创叶片机器人芽芽",
            ["UiText012"] = "芽芽：原创叶片机器人，点击可互动",
            ["UiText013"] = "状态轻松",
            ["UiText014"] = "5小时重置",
            ["UiText015"] = "时",
            ["UiText016"] = "分",
            ["UiText017"] = "秒",
            ["UiText018"] = "等待同步",
            ["UiText019"] = "5小时使用限额",
            ["UiText020"] = "已用 --%",
            ["UiText021"] = "重置时间等待上报",
            ["UiText022"] = "每周使用限额",
            ["UiText023"] = "暂未检测到使用限额",
            ["UiText024"] = "芽芽智能体会继续检查",
            ["UiText025"] = "芽芽智能体 · 正在分析",
            ["UiText026"] = "读取额度后给出建议",
            ["UiText027"] = "随机重置",
            ["UiText028"] = "等待官方上报",
            ["UiText029"] = "芽芽快报 · Tibo",
            ["UiText030"] = "查看原帖 ↗",
            ["UiText031"] = "Tibo 动态 · 正在检查",
            ["UiText032"] = "正在获取动态与本账户状态",
            ["UiText033"] = "正在连接…",
            ["UiText034"] = "开机启动",
        });

    public string Get(string source) => !IsChinese && EnglishBySource.TryGetValue(source, out var english)
        ? english : source;

    public string UpdateState(bool enabled) => IsChinese
        ? (enabled ? "开启" : "关闭") : (enabled ? "On" : "Off");

    public string Format(string sourceFormat, params object?[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, Get(sourceFormat), arguments);

    // Only original policy/service text enters here, at its display write boundary.
    // Anchored patterns adapt known formats, never traverse controls or translate arbitrary data.
    public string Translate(string source)
    {
        if (IsChinese) return source;
        if (EnglishBySource.TryGetValue(source, out var exact)) return exact;
        if (source.StartsWith("原帖：", StringComparison.Ordinal)) return "Original: " + source[3..];
        if (source.Contains(" + ", StringComparison.Ordinal))
            return string.Join(" + ", source.Split(" + ", StringSplitOptions.None).Select(Translate));
        foreach (var (pattern, format) in PolicyFormats)
        {
            var match = pattern.Match(source);
            if (!match.Success) continue;
            var values = match.Groups.Cast<Group>().Skip(1).Select(group => (object)Translate(group.Value)).ToArray();
            return string.Format(CultureInfo.InvariantCulture, format, values);
        }
        return source;
    }

    // Exception details from Windows/third parties stay verbatim if not owned by this application.
    public string ErrorMessage(string message)
    {
        if (IsChinese) return message;
        var firstLine = message.IndexOf('\n');
        if (firstLine >= 0) return Translate(message[..firstLine].TrimEnd('\r')) + message[firstLine..];
        var parameter = message.IndexOf(" (Parameter", StringComparison.Ordinal);
        return parameter < 0 ? Translate(message) : Translate(message[..parameter]) + message[parameter..];
    }

    private static readonly (Regex Pattern, string Format)[] PolicyFormats =
    [
        Rule(@"^感谢 ChatGPT · 可重置(\d+)次$", "Thanks ChatGPT · {0} resets available"),
        Rule(@"^已有 (\d+) 次可用机会，需要时可以放心使用。$", "{0} resets available. Use them when needed."),
        Rule(@"^约 (\d+) 天后重置$", "reset in ~{0}d"),
        Rule(@"^约 (\d+) 小时后重置$", "reset in ~{0}h"),
        Rule(@"^仅保留必要任务，(.+)。$", "Essential tasks only; {0}."),
        Rule(@"^本周余量偏低，(.+)。$", "Weekly quota low; {0}."),
        Rule(@"^建议放慢非必要任务，(.+)。$", "Slow optional tasks; {0}."),
        Rule(@"^额度充足，可以正常推进，(.+)。$", "Plenty of quota; {0}."),
        Rule(@"^当前消耗正常，(.+)。$", "Usage on track; {0}."),
        Rule(@"^已识别 (.+)，正在连接…$", "Detected {0}; connecting..."),
        Rule(@"^重置(\d+)次$", "{0} resets"),
        Rule(@"^(\d{1,2})月(\d{1,2})日 (\d{2}:\d{2})$", "{0}/{1} {2}"),
        Rule(@"^Tibo 最新已核实动态 · (.+)$", "Tibo latest verified post · {0}"),
        Rule(@"^本轮周周期：(.+) 开始 · 重置已完成$", "Weekly cycle: {0} start · reset complete"),
        Rule(@"^社区预计最晚北京时间 (.+) 前$", "Community estimate: by {0} (Beijing UTC+8)"),
        Rule(@"^社区预计北京时间 (.+) 后$", "Community estimate: after {0} (Beijing UTC+8)"),
        Rule(@"^(.+) · 正在加密监测$", "{0} · Monitoring closely")
    ];

    private static (Regex, string) Rule(string pattern, string format) =>
        (new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)), format);
}
