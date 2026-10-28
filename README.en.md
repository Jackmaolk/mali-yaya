# Mali Yaya · Codex Usage Desktop Companion

[简体中文](README.md)

A compact Windows WPF companion focused on Tibo reset announcements, Beijing-time reset windows, account-confirmed recovery reminders, and quota countdowns. Version 2026.10.28.3 is an unofficial source preview, not an OpenAI product or a signed production installer. Repository: [Jackmaolk/mali-yaya](https://github.com/Jackmaolk/mali-yaya). Publicly visible source does not imply a license to redistribute or commercialize it; see [rights](RIGHTS.md).

## Network Comes First

The widget needs working access to Codex and the public activity/translation services. If your network cannot reach them directly, first connect your own proxy tool to a working node and configure it as appropriate. Directly reachable networks need no additional proxy. This software supplies no proxy subscription or nodes.

External HTTP requests use the default Windows/.NET proxy behavior; the Codex child also inherits proxy environment variables. A stale HTTP_PROXY, HTTPS_PROXY or ALL_PROXY can conflict with a changed proxy-tool port. There is no universal required port: 7897 is only one possible local setup. Restart this widget normally after changing its launch environment; do not force-quit Codex or reboot Windows for it.

Automatic proxy-port scanning, airport/node switching, Clash/LLG control and system-network repair are not built into this widget. Any separately configured personal network guardian is outside this repository and its packages. A listening port, a working browser or a cached quota percentage does not prove a healthy Codex streaming connection. Account quota, activity, translation and GitHub can fail independently. Never disable TLS verification or security software as a workaround.

## Get Started

1. Install a compatible .NET 9 Desktop Runtime on Windows x64. This runtime is near the end of support; migrating to a supported LTS and using current security patches remain release requirements.
2. Install and sign in to your own Codex CLI, or use an accessible CLI bundled with Codex Desktop/your IDE. A browser login alone does not guarantee that this CLI is signed in. API-key authentication is not the same as a ChatGPT subscription.
3. Extract the complete verified release folder and run CodexUsageWidget.exe. No terminal window is required, but the widget naturally has a background process.

No production GitHub download release has been published yet. Creating a repository or pushing source does not create an installer or enable automatic updates. Do not install another project's release as an update for this widget. Extract all runtime files to a writable local directory; current state storage is not suitable for a read-only installation folder.

## Language and Appearance

The interface follows the Windows display language on startup: zh cultures use Chinese; English and other cultures use English. It does not change Windows settings, infer language from IP/proxy, or call a translation service for its UI. Restart the widget after changing the OS display language.

The original leaf robot, Yaya, reacts to quota conditions and reset states. The generated mint-green app icon is embedded in the launcher. Internal runtime filenames stay stable for updates. For this personal installation, explicitly running InstallDesktopEntry.exe creates a desktop launcher named 码力芽芽.exe on Chinese systems or Mali Yaya.exe otherwise; it backs up the old entry first. It never needs to reboot Windows or Codex. See [icon provenance](docs/ICON-DESIGN.md).

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

A fresh weekly reading at 100% remaining immediately ends account verification under the current product rule. If the 100% moment was missed, new-cycle timestamps, decreased usage and other validated account evidence can still identify recovery. A 95% reading alone is not sufficient. Recovery does not prove which announcement caused it or remove separate five-hour/model restrictions.

The stacked flip clock is the account's actual five-hour window, not the announcement timer. Only a newly reported account resetsAt changes the deadline. Cached reliable deadlines continue ticking locally during failures; at zero the clock waits for a real new endpoint rather than adding another five hours.

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
