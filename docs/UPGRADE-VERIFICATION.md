# 2026.10.31.1 Lifecycle Hotfix

A scoped, redacted Windows .NET Runtime event from the old .NET 9 installation
identified InvalidOperationException in MainWindow.Window_Closing / VerifyNotClosing.
The same defect was independently reproduced in current .NET 10 source, with four
failing Chinese/English unshown/native-window cases. A fifth regression reproduced
a late Loaded callback restarting timers during shutdown; a busy-client repeated
close control case already passed. All six cases pass after deferring final Close
to the Dispatcher and rejecting Loaded after stopping. Full local suite: 737 passed,
0 failed, 0 skipped. The native-window tests disable product startup services and
use isolated data; they do not read account credentials, launch CLI or send HTTP.

This evidence does not prove that every reported startup failure has that cause.
The owner's desktop shortcut still names an older external installation. Source
repairs and a GitHub release do not silently replace that local entry. No private
crash log, account state or local paths are included in exported source.

Exact-SHA hosted CI, strict dependency audit, runtime/package verification and
public download digest checks are required before preview publication. All prior
clean-machine, account, mixed-DPI, outbound-network and update/rollback acceptance
gates remain pending; no stable release or coverage percentage is asserted.

---

# 2026.10.29.1 Preview Integration

The owner explicitly authorized merging PR #3 into the upgrade branch, merging
PR #1 into main, rerunning exact-main CI and publishing a non-latest prerelease.
The resolved homepage preserves all 15 authorized images and the Codex MaliYaya
display title, with current .NET 10 instructions. Source packaging now checks
both 15-image galleries and rejects a modified failure-state preview (10 checks).
All 117 source files, including two individually hash-pinned failure images,
remain allowlisted; no arbitrary image or private runtime export is permitted.
The runtime code is unchanged from the 731-test hardening source. Hosted CI
results and final asset digests must be recorded against the exact merged SHA.
No stable acceptance, real-account result or live local replacement is claimed.

---

# 2026.10.29.1 Hardening Follow-Up

Branch: `fix/monitor-hardening`, based on the reviewed .NET 10 candidate. The public homepage documentation remains independently merged; no application merge or live replacement is part of this change.

## Reproduced And Fixed

- Boundary tests reproduced 7 failures out of 9: missing usage silently became zero, a long-duration absolute-value overflow, null/invalid credit expiry data, and cached notices never expiring.
- Client tests reproduced 7 failures: invalid snapshots were published, malformed usage notifications were accepted, reset opportunities crossed reconnects, and count-only reports retained an unrelated expiry.
- Four additional advice-boundary tests reproduced invalid Unix time, date subtraction and duration overflow; optional invalid reset timestamps now become unknown without losing valid quota.
- Three UI regressions also reproduced invalid Unix/Beijing-offset date crashes; expiry formatting now retains the count without an invalid date.
- Added positive checks for explicit 0/42/100 usage, valid neighboring credits and confirmed recovery surviving announcement expiry.
- Bilingual WPF tests verify clock-only expiry sends no HTTP. Connection-boundary tests verify same-connection behavior is preserved while a fresh transport cannot reuse recovery/countdown state. Random markers are never serialized or restored; no account identifier or credentials are read.

## Current Local Verification

- Full .NET 10 suite: **731 passed, 0 failed, 0 skipped** (38 added).
- No dependency changes. The local NuGet audit source still emits NU1900; no current local audit success or coverage percentage is claimed.
- Runtime-contract fixtures (41), isolated launcher checks (26), icon sizes (9), package checks (3) and source-export safety checks (8) passed locally. The final artifact is separately rebuilt after the last date-boundary fix. Hosted CI must be associated with the exact uploaded SHA rather than inferred from the older candidate.
- This is not a clean-machine, real account-switch, signed-distribution or live hot-update acceptance result. New-connection baseline reset is intentionally conservative: it may miss a recovery that happened between connections rather than infer it across unbound accounts.

---

# 2026.10.28.5 Verification Record

Feature branch: `feat/release-readiness-net10`. Main is not overwritten or merged.
This is a review candidate, not authorization to publish a stable release.

## Implemented

1. Initial weekly quota is a baseline, never a just-completed reset. Valid,
   witnessed recovery or a recent forward cycle transition supplies evidence.
2. Application/tests target .NET 10. SDK selection, assembly/file identity,
   release diagnostics, manifest and updater runtime configuration agree.
3. Current-user LocalAppData holds settings, cache and per-install runtime data.
   Minimal health records are separate from quota/translation data. Legacy
   private state is not imported; old files remain untouched. Restart waits for
   fresh account data instead of restoring an unbound account countdown.
4. Push/PR Windows CI uses read-only permissions, pinned actions and strict
   transitive vulnerability audit. Trusted-main manual release workflow creates
   a draft only after review/acceptance gates, never a public stable release.
5. Tibo monitoring/translation is on for missing first-run preferences, independent
   of minimal mode. Saved opt-outs persist; malformed/unsafe settings stay offline. Off cancels
   in-flight work/retries and rejects stale sessions. Quota/countdown continue.
6. Update handoff uses explicit, one-shot UI approval; cancellation/timeout leaves
   no install permission. Old updater callbacks cannot overwrite opt-out state.
7. Framework-dependent Windows candidate ZIP passes local package checks. Original
   Yaya is retained; README galleries add 12 labeled offline Chinese/English
   previews, separate from the existing consented real screenshot.

## 2026.10.28.5 Follow-Up

The owner requested first-run default-on Tibo monitoring. Two new missing-settings
regression cases failed before the fix and pass afterward. Saved-off/full-size and
empty-file cases are covered. Offline WPF tests explicitly seed networking off in
the isolated test directory, so changing product defaults does not send live HTTP.
The separate automatic-update feature remains off by default.

Local desktop startup was repaired using a per-user, unmodified .NET 10.0.14
runtime and a versioned application directory, without changing system .NET 9.
The generic candidate ZIP remains framework-dependent. This local deployment is
not evidence of clean-machine or GitHub-hosted update acceptance.

## Remote CI Follow-Up (2026-10-29)

The first push/PR run passed source safety, launcher, runtime-contract and
transitive-audit steps, but two countdown tests assumed Chinese UI on an English
runner (689 passed, 2 failed). The tests now explicitly exercise zh-CN and en-US,
checking the corresponding localized labels without changing product behavior or
skipping either language. The expanded local suite has 693 tests. Remote full
verification must be rerun on the follow-up commit.

## Executed Locally

- .NET SDK 10.0.403, Windows Desktop/Core runtime 10.0.14.
- Full .NET 10 tests: **693 passed, 0 failed, 0 skipped**.
- Runtime configuration contract: **41 synthetic cases passed**.
- Native portable launcher: **26 isolated cases passed**.
- Icon validation: **9 sizes passed**, including embedded launcher icon.
- Release package: **3 checks passed** (inventory/hash, no overwrite, version mismatch).
- Build-only diagnostic: identity/runtime JSON correct, isolated payload unchanged,
  no fixture directories/runtime/cache created. This is not OS network observation.
- Source export: **8 safety checks passed**, including secret-shaped synthetic
  fixtures, runtime exclusion, pinned screenshot hashes and unknown-image rejection.
- `git diff --check` passed. No account, auth or private runtime files inspected.

## Not Passed / Not Measured

- Local NuGet vulnerability data: **unavailable (NU1900, TLS/Schannel credential
  initialization error)**. Cached packages enabled compilation; this is not a
  successful current vulnerability audit. CI keeps NU1900-NU1905 as errors.
- Coverage collection was attempted with the existing Microsoft collector. It
  reported that its profiler could not initialize and produced no usable data.
  **No coverage percentage or 80% target achievement is claimed.**
- This old Windows build fails the SDK's CET-enabled compiler apphost. Local
  developer-only tooling invoked the same official `csc.dll` through `dotnet exec`
  instead; no Windows security policy, runtime binary or global SDK was changed.
  Official patched Windows CI/clean-machine acceptance remains required.
- Real Plus/Pro integration, outbound-request observation after opt-out, clean
  no-SDK installation, real English Windows/mixed DPI, live update/rollback and
  signed-distribution behavior remain pending in [release acceptance](RELEASE-ACCEPTANCE.md).
- Public visibility does not grant an unspecified software license. Owner's
  licensing/signing decisions and third-party terms remain separate release gates.

## Delivery Boundary

Open a PR without automatic merge. Do not publish this branch as stable or reuse
the existing .NET 9 preview tag. Real acceptance and successful exact-main CI
must precede any separately approved stable publication. Local package checks
do not prove GitHub-hosted workflow execution or live update acceptance.
