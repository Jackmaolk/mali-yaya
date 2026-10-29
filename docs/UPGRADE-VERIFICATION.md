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

## Executed Locally

- .NET SDK 10.0.403, Windows Desktop/Core runtime 10.0.14.
- Full .NET 10 tests: **691 passed, 0 failed, 0 skipped**.
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
