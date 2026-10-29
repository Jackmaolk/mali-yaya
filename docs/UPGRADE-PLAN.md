# Release readiness upgrade

Work is isolated on `feat/release-readiness-net10`; do not merge or overwrite main automatically.

## Ordered delivery

1. Reproduce and fix initial full-week readings falsely announcing a just-completed reset. Baseline is not an event; only fresh account transitions can celebrate, once per cycle.
2. Target .NET 10 LTS and align project/test frameworks, runtime diagnostics, update manifest checks, packaging scripts and version contracts.
3. Move runtime state, countdown checkpoints and translation cache into current-user LocalAppData. Rehydrate fresh account data rather than importing unbound private legacy state; preserve test injection and old files, never migrate credentials or follow links into untrusted locations.
4. Add least-privilege push and PR Windows CI, with isolated fixtures, build/tests/audit and release package checks; never run untrusted PR code with secrets or write tokens.
5. Add independent Tibo networking and compact-mode preferences, persistence, localization and UI/network regression tests. Preserve the original Yaya character and architecture; remove obsolete display names while keeping necessary legacy migration aliases.
6. Produce a verifiable .NET 10 Windows release candidate, checksums and acceptance checklist. Publish a stable Release only from approved, merged source after required gates pass; do not tag unreviewed branch code as stable. Open a PR with exact results and remaining real-machine acceptance gaps.

## Verification and privacy

- New behavior is covered by failing tests before implementation, followed by the full suite.
- Tests use synthetic data and isolated directories; do not inspect or copy auth.json, credentials, real account state, private caches or user environment tokens.
- Keep update integrity checks, no forced Codex/Windows restarts, no automatic main merge and no disabling TLS/audit/CI security checks to obtain a green result.
- SDK installations are project-local and must not enter the repository or alter the user's global runtime configuration.
- Actual coverage and real-machine acceptance are reported, never inferred from the number of tests.

## Initial review

The baseline's `TiboResetConfirmationPolicy` accepts UsedPercent=0 without previous readings or valid reset metadata. Aligned initial five-hour/week windows can also report a false reset. Existing tests encode this incorrect behavior and must be changed alongside new regressions.

The baseline release is .NET 9, runtime/cache files live beside the executable, CI is manual-only, and Tibo has no independent opt-out. The earlier Windows ZIP is explicitly a prerelease, not proof of stable-release readiness.
