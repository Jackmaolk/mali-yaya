# Codex MaliYaya · Codex Usage Desktop Companion

[简体中文](README.md) · [Current preview downloads](https://github.com/Jackmaolk/mali-yaya/releases) · [Feedback](https://github.com/Jackmaolk/mali-yaya/issues/new)

**Know your allowance. Follow reset announcements. Celebrate account-confirmed recovery.**

An independent Windows desktop companion with an original leaf robot, a stacked flip countdown, weekly quotas, Tibo activity and Beijing-time notices. **Not an official OpenAI product.** The Codex prefix is a display-title change only: the repository, update source and runtime filenames are unchanged.

## What Changed

**Startup/exit hotfix:** avoid recursive WPF Close calls when cleanup completes inline, and ignore late Loaded callbacks after shutdown begins. Six new isolated lifecycle cases include actual native-window show/close without starting CLI or HTTP services. Old shortcuts can still launch an old build; extract this complete package and check the target.

**2026.10.31.1 / .NET 10 is a Windows x64 preview, not a stable release.** It fixes first-reading reset false positives, unsafe optional-storage startup failures and canceled update handoffs. It adds .NET 10/LocalAppData migration, independent Tibo networking and minimal-mode preferences, push/PR CI and a 15-image bilingual gallery.

737 automated tests and hosted dependency-audit steps passed; coverage and real-machine acceptance remain outstanding. See the **[complete bilingual changelog](CHANGELOG.md)** for fixes, limitations and verification results.

**Right-click the widget's title bar or panel background for CLI connection, update repository, Tibo monitoring and minimal-mode settings.** Tibo monitoring is on for first-time use; automatic updating is still off by default. These are independent switches.

A compact Windows WPF companion focused on Tibo reset announcements, Beijing-time reset windows, account-confirmed recovery reminders, and quota countdowns. Version 2026.10.31.1 is an unofficial .NET 10 preview, not an OpenAI product or a signed production installer. Repository: [Jackmaolk/mali-yaya](https://github.com/Jackmaolk/mali-yaya). Publicly visible source does not imply a license to redistribute or commercialize it; see [rights](RIGHTS.md).

## See It In Action: 7 States, Both Languages

**15 images: 14 bilingual state demonstrations and one consented real screenshot.** Demonstrations are rendered by the actual WPF interface using isolated, offline synthetic data. Each is labeled; none is evidence of a live announcement, account benefit or current reset.

The demonstrations show the .NET 10 interface; values and posts are synthetic or capture-time data, not current account evidence. Images wrap on narrow screens; tap to view full size.

### Reset confirmed · Yaya celebrates

<p>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-confirmed-zh.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-confirmed-zh.png" alt="重置完成 · 芽芽庆祝：中文离线界面演示，模拟数据" width="280" /></a>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-confirmed-en.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-confirmed-en.png" alt="Reset confirmed · Yaya celebrates: English offline UI demonstration with synthetic data" width="280" /></a>
</p>

Left/top: Chinese. Right/bottom: English. Tap an image for full size.

### Reset announced · Beijing time

<p>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-planned-zh.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-planned-zh.png" alt="提前预告 · 北京时间：中文离线界面演示，模拟数据" width="280" /></a>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-planned-en.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-planned-en.png" alt="Reset announced · Beijing time: English offline UI demonstration with synthetic data" width="280" /></a>
</p>

Left/top: Chinese. Right/bottom: English. Tap an image for full size.

### Connection failure · Keep last known data

<p>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-unavailable-zh.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-unavailable-zh.png" alt="联网失败 · 保留已知数据：中文离线界面演示，模拟数据" width="280" /></a>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-unavailable-en.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-unavailable-en.png" alt="Connection failure · Keep last known data: English offline UI demonstration with synthetic data" width="280" /></a>
</p>

Left/top: Chinese. Right/bottom: English. Tap an image for full size.

### Everyday quotas · Flip countdown

<p>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-normal-zh.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-normal-zh.png" alt="日常额度 · 翻页倒计时：中文离线界面演示，模拟数据" width="280" /></a>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-normal-en.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-normal-en.png" alt="Everyday quotas · Flip countdown: English offline UI demonstration with synthetic data" width="280" /></a>
</p>

Left/top: Chinese. Right/bottom: English. Tap an image for full size.

### Low allowance · Clear warning

<p>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-low-zh.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-low-zh.png" alt="额度偏低 · 醒目提醒：中文离线界面演示，模拟数据" width="280" /></a>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-low-en.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-low-en.png" alt="Low allowance · Clear warning: English offline UI demonstration with synthetic data" width="280" /></a>
</p>

Left/top: Chinese. Right/bottom: English. Tap an image for full size.

### Minimal mode · A smaller desktop footprint

<p>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-minimal-zh.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-minimal-zh.png" alt="极简模式 · 更小的桌面占用：中文离线界面演示，模拟数据" width="280" /></a>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-minimal-en.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-minimal-en.png" alt="Minimal mode · A smaller desktop footprint: English offline UI demonstration with synthetic data" width="280" /></a>
</p>

Left/top: Chinese. Right/bottom: English. Tap an image for full size.

### Tibo switched off · Quota still refreshes

<p>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-offline-zh.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-offline-zh.png" alt="主动关闭 Tibo · 额度照常刷新：中文离线界面演示，模拟数据" width="280" /></a>
<a href="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-offline-en.png"><img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-offline-en.png" alt="Tibo switched off · Quota still refreshes: English offline UI demonstration with synthetic data" width="280" /></a>
</p>

Left/top: Chinese. Right/bottom: English. Tap an image for full size.

## Consented Real Running Screenshot

<img src="https://raw.githubusercontent.com/Jackmaolk/mali-yaya/main/docs/images/widget-reset-complete-zh.png" alt="User-provided real Chinese screenshot: original Yaya, account quotas, flip countdown and confirmed recovery" width="463" />

This separate user-provided capture shows a real running widget. Its values, subscription, posts and timestamps reflect that capture only, not installation defaults or promises to another account. No older third-party character artwork is presented as current product imagery.

## Network Comes First

The widget needs working access to Codex and the public activity/translation services. If your network cannot reach them directly, first connect your own proxy tool to a working node and configure it as appropriate. Directly reachable networks need no additional proxy. This software supplies no proxy subscription or nodes.

External HTTP requests use the default Windows/.NET proxy behavior; the Codex child also inherits proxy environment variables. A stale HTTP_PROXY, HTTPS_PROXY or ALL_PROXY can conflict with a changed proxy-tool port. There is no universal required port: 7897 is only one possible local setup. Restart this widget normally after changing its launch environment; do not force-quit Codex or reboot Windows for it.

Automatic proxy-port scanning, airport/node switching, Clash/LLG control and system-network repair are not built into this widget. Any separately configured personal network guardian is outside this repository and its packages. A listening port, a working browser or a cached quota percentage does not prove a healthy Codex streaming connection. Account quota, activity, translation and GitHub can fail independently. Never disable TLS verification or security software as a workaround.

## Get Started

1. Install [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) on a supported, fully patched Windows system. Development requires the .NET 10 SDK. A plain .NET or ASP.NET Runtime is not sufficient. Do not disable CET/security protections on outdated Windows.
2. Install and sign in to your own Codex CLI, or use an accessible CLI bundled with Codex Desktop/your IDE. A browser login alone does not guarantee that this CLI is signed in. API-key authentication is not the same as a ChatGPT subscription.
3. Extract the complete verified release folder and run CodexUsageWidget.exe. No terminal window is required, but the widget naturally has a background process.

[Download the 2026.10.31.1 preview](https://github.com/Jackmaolk/mali-yaya/releases/tag/v2026.10.31.1) (Windows x64 / .NET 10; the link may not be live while publication is being prepared). This is not a signed stable installer; read the remaining acceptance items in its release notes. Creating a repository or pushing source does not create an installer or enable automatic updates. Do not install another project's release as an update for this widget. Runtime data/cache now use current-user LocalAppData, with separate per-install health and private state. Binary replacement still requires a writable install directory. Old .NET 9 updaters cannot accept .NET 10 updates: install Desktop Runtime 10 and manually extract the new package first.

## Language and Appearance

The interface follows the Windows display language on startup: zh cultures use Chinese; English and other cultures use English. It does not change Windows settings, infer language from IP/proxy, or call a translation service for its UI. Restart the widget after changing the OS display language.

The original leaf robot, Yaya, reacts to quota conditions and reset states. The generated mint-green app icon is embedded in the launcher. Internal runtime filenames stay stable for updates. For this personal installation, explicitly running InstallDesktopEntry.exe creates a desktop launcher named 码力芽芽.exe on Chinese systems or Mali Yaya.exe otherwise; it backs up the old entry first. It never needs to reboot Windows or Codex. See [icon provenance](docs/ICON-DESIGN.md).

## Right-Click Menu

**Right-click the running widget's title bar or panel background**, not the desktop EXE or shortcut. Menu labels follow the Windows display language.

| Menu Item | Behavior And Limits |
| --- | --- |
| Detect Codex CLI again | Clears the manually selected CLI path, checks trusted known installation locations again and attempts to connect. It does not scan the whole disk, install software or sign you in. |
| Select Codex CLI... | Select a trusted, architecture-compatible native CLI `.exe`, not the desktop application, a shortcut or a `.cmd` / `.ps1` script. Saves the selection and attempts to connect. |
| View Codex connection info | Shows the CLI source, version and path, or a not-detected message. It does not display or export credentials. |
| Automatic updates (every 24 hours) | Check to enable, uncheck to disable; off by default. Configure this software's specific repository first. Attempts are limited to once per 24 hours, including failures. |
| Configure update repository... | Enter a specific repository such as `Jackmaolk/mali-yaya`, not a profile URL or token. Saving keeps automatic updates off; explicitly check the update switch afterward if wanted. |
| View update status | Shows the current version, configured repository, update switch and latest status. This is an information dialog, not a manual-update button that bypasses the 24-hour interval. |
| Tibo online monitoring (with translation) | Enables public activity queries, post verification and on-demand translation; on for first-time use. Turning it off stops this networking, not account quota refresh or the local countdown. |
| Minimal mode | Hides news, advice and auxiliary details while keeping main quotas, Yaya and the countdown. Uncheck to restore the full panel. It does not change the Tibo switch. |

The menu also has a disabled, read-only update-status line whose text changes with update state. Tibo and minimal-mode choices are saved independently for the next launch. Upgrades preserve an explicit Tibo opt-out.

**Refresh, fold and close are top-panel buttons, not context-menu items.** Startup at Windows sign-in uses the bottom checkbox. Folding or minimal mode does not stop background monitoring.

## Independent Networking and Minimal Mode

Tibo monitoring is on by default on first launch: it automatically checks public activity, verifies posts and translates on demand. Right-click **Tibo online monitoring (with translation)** to turn it off or on. A saved off preference is preserved across upgrades; invalid or unsafe settings fail closed. Turning it off cancels requests/retries and rejects late results, without stopping account quota refresh or the local countdown. Already sent requests cannot be recalled.

**Minimal mode** hides the news/advice/details, keeping quotas, the original robot and countdown. It is a separate saved preference, not a networking switch. Folding the panel also does not turn networking off.

## Everyday Use

- Refresh reads current account quota. Failures are marked and do not turn unknown data into zero.
- Fold/expand keeps monitoring active; Close stops this widget's work.
- Drag the panel to move it. Other applications can cover it; it is not permanently topmost.
- The subscription badge uses the returned planType (such as PLUS or PRO); missing data shows --.
- Five-hour/weekly cards are shown only if the account actually reports those windows.
- The stacked flip countdown uses the account's resetsAt timestamp. It does not invent another five hours at zero.
- Reset opportunities are read from account data, never fabricated or automatically spent.
- Tibo headlines distinguish announcements from an actual account reset. Original post content is preserved; public activity and machine translations are not official account evidence.
- Reset times are presented in Beijing time (UTC+8), not silently converted to the OS timezone.

There is no CDP injection, API-account/key dashboard, token-history chart, reset-probability model, or automatic task-resume feature in this widget.

## Tibo Announcements and Recovery Reminders

The monitored author is [Tibo / @thsottiaux](https://x.com/thsottiaux), not any account with a similar display name. The widget gets candidate posts/windows from a community feed, then checks constrained author/post URLs and timestamps through public FxTwitter/VxTwitter mirrors. It does not sign in to X, subscribe to a real-time official X stream, fetch every post, or train a prediction model. Third-party indexing and verification can lag or fail.

| State | What it means |
| --- | --- |
| Reset announced | A verified reset-related post has a currently valid source-provided window. This is an estimate, not a promise for your account. |
| Bonus reset mentioned | A public post discusses extra reset opportunities. Your actual count/expiry must come from account data; redemption may require manual action in the official app. |
| Observed publicly / checking account | A public report is not yet backed by enough account evidence. |
| Account reset complete | Fresh account quota satisfies the recovery rules. The panel updates and Yaya celebrates once per recognized cycle. |
| Latest/recent verified activity | With no effective announcement, show a verified recent post and its time. Chinese UI adds a cached machine-translated brief below the original; English UI preserves the original and skips Chinese translation requests. |
| Unavailable / expired | No trustworthy current confirmation. Do not treat old announcements or cached posts as new evidence. |

These are in-widget cards, text and character animations, not Windows toast, audio, email or messaging notifications. Monitoring stops when the widget is closed or Windows is asleep/off; reconnecting does not guarantee replay of every missed event. Reset opportunities are not payments, paid top-ups or Credits purchases, and the widget never buys or redeems them for you.

### Timing and Confirmation

All displayed reset times use Beijing time (UTC+8). US daylight-saving changes mean a fixed year-round time offset is incorrect. A usable source timestamp/window is converted; ambiguous text such as "tomorrow" is not independently turned into a guaranteed minute-accurate deadline.

The first reading, including the first sample after each new CLI connection, is a baseline, not a reset event, even at 100% remaining. With valid weekly metadata, a witnessed recovery from previously consumed quota to full confirms restoration. If that instant was missed, usage must decrease while the same-duration weekly deadline advances and the new cycle begins within six hours. Backward deadlines, missing metadata or a 95% reading alone are not enough. Recovery does not prove which announcement caused it or remove separate five-hour/model restrictions.

The stacked flip clock is the account's actual five-hour window, not the announcement timer. Only a newly reported account resetsAt changes the deadline. Within the same connection, cached reliable deadlines continue ticking locally during failures. A new CLI connection discards the prior connection's unbound countdown, recovery conclusion and reset-opportunity cache and waits for fresh data; at zero the clock waits for a real new endpoint rather than adding another five hours.

### Polling Schedule

| Situation | Activity target interval | Quota target interval |
| --- | --- | --- |
| Normal monitoring | 5 minutes | 15 seconds |
| Announced window more than 2 hours away | 3 minutes | 15 seconds |
| 15 minutes to 2 hours away | 1 minute | 5 seconds |
| Within 15 minutes, in-window, or recent confirmation pending | 20 seconds | 5 seconds |
| Recovery confirmed | 3 minutes | 15 seconds |
| Activity errors | Backoff: 30, 60, 120, 240, 300 seconds | 15 seconds |

Activity polling has approximately +/-15% jitter and short verification caches; these are targets, not real-time guarantees. Long-expired notices return to lower frequency. Hover for source/check timing. The flip clock updates locally each second without requesting X or Codex each second.

Sources: codex.gussuriworks.com/api/current, api.fxtwitter.com, api.vxtwitter.com, and FxEmbed machine translation. All are separate from official account evidence. No 12/24/48/72-hour probability calculations are implemented. Avoid exhausting your allowance just because a public estimate suggests a reset may be near.

## Connection

Right-click for automatic Codex CLI detection, manual executable selection, and connection information. Detection is limited to known local native executable locations, not a whole-disk scan. It does not copy auth.json, change proxy settings, install software or sign you in automatically.

A high remaining percentage does not guarantee that a particular model, login session, proxy or service is currently available.

## Optional Updates

Automatic updating is off and unconfigured by default. Right-click to configure the specific owned repository, then explicitly enable updating. A profile/Projects URL is not a release repository. Only public releases readable without credentials are supported; do not provide GitHub tokens.

A private source repository is not a usable anonymous update source. The repository must be publicly readable and contain a correctly packaged formal Release; source commits and prereleases alone do not trigger this updater.

When enabled and configured, attempts are limited to once per 24 hours, including failures and restarts. The updater accepts only a newer formal release with an exact matching Windows ZIP name and GitHub SHA-256 digest. It validates archive paths, the five-file allowlist and per-file hashes before applying anything.

A separate local helper waits for this widget to exit, backs up files, atomically replaces runtime files and waits for the new version to become ready. A recovery journal supports interrupted updates. Recoverable failures restore the previous files; persistent disk/permission problems may require manual recovery. Codex and Windows are not restarted.

GitHub's digest is an integrity check, not an independent publisher signature. Real Release download/handoff testing, code signing, runtime maintenance, installation and uninstall acceptance remain necessary before public distribution. See [the update contract](docs/AUTO-UPDATE.md).

## Privacy and Development

The widget does not ask for your OpenAI/GitHub tokens. Public post/translation services and opt-in GitHub updates are separate from local account reads. Runtime state may contain private usage information and must not be uploaded.

Run dotnet test against tests/CodexUsageWidget.Tests/CodexUsageWidget.Tests.csproj and use scripts/Test-SourcePackaging.ps1, Test-Launcher.ps1, Test-Icon.ps1 and Test-ReleasePackage.ps1 for the corresponding checks. build.ps1 does not change the desktop unless -InstallDesktop is explicitly supplied.

[Privacy](PRIVACY.md) · [Security](SECURITY.md) · [Rights](RIGHTS.md) · [Review status](docs/PUBLISHING.md)
