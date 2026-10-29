# Release Acceptance

Status: **PENDING. Not approved for a stable release.** Automated tests, a draft
candidate, and developer-machine results do not satisfy the real-machine gates.
Record the exact merged main commit, BuildVersion, ZIP SHA256, OS build/runtime
patch, CLI version, date, tester and independent reviewer in an acceptance issue.
Do not include tokens, account identifiers, private paths, settings or auth dumps.

## Automated Gates

- [ ] Push/PR Windows CI passes: source export/inventory, synthetic launcher and
  runtime-contract fixtures, full .NET 10 tests, release publish build, isolated
  `--diagnose-build`, icon and release-package inventory/hash/version checks.
- [ ] CI restore has transitive auditing enabled. NU1900 through NU1905 are
  errors through `ContinuousIntegrationBuild=true`; unavailable audit data is
  not a passing security check. Do not disable audit or suppress warnings.
- [ ] The exact main SHA has a successful push run of `verify.yml`; PR merge-ref
  or feature-branch results alone do not qualify. Branch protection requires
  the Windows verification check and review before merging source/workflows.
- [ ] Review the runtime contract: framework-dependent net10.0, exactly
  Microsoft.NETCore.App and Microsoft.WindowsDesktop.App 10.0.x; no major
  roll-forward, injected probing paths, oversized config or unexpected files.
- [ ] Independently verify ZIP name `mali-yaya-<BuildVersion>-windows-x64.zip`,
  tag `v<BuildVersion>`, diagnostic/manifest identity and every SHA256 value.

CI uses only read permissions for pushes/PRs, SHA-pinned existing checkout and
setup-dotnet actions, no persisted checkout credentials, and no account secrets.
The build diagnostic uses only build identity and an isolated child environment;
it must not resolve the CLI, read settings/auth, start the normal GUI, or connect.
Fixture inventory checks are not proof of OS-level network isolation. Confirm
zero requests with local process-scoped observation on the clean machine below.

## Real Windows Acceptance (All Pending)

- [ ] Clean supported x64 Windows VM/machine with all OS/security updates,
  including required CET support; install the current patched .NET 10 Windows
  Desktop Runtime. Test with a normal non-admin user and no developer SDK.
  Missing runtime gives a clear safe failure rather than silently using .NET 9/11.
- [ ] Run the delivered launcher by double-click from a path with spaces and
  non-ASCII characters. No console flashes, duplicate windows or broken icon.
  Confirm readiness without an account and without a network response.
- [ ] Run `--diagnose-build` with process-scoped network observation: correct
  product/version/runtimeMajor=10, zero outbound requests, no settings/auth reads,
  no GUI/timers/CLI child processes and no runtime/cache writes. Do not open,
  copy, inspect or upload credential files to perform this check.
- [ ] With explicit tester consent, exercise normal Codex CLI integration using
  already signed-in Plus and Pro test accounts. Compare displayed subscription
  and quota to the supported CLI response; do not infer subscription from a
  screenshot or an API key. No credential-reading test or auth-file inspection.
  Keep this manual integration separate from no-account fixture tests.
- [ ] Disconnect/reconnect the tester's proxy and network using process-local
  or isolated-VM configuration, never the developer's global environment.
  Verify bounded failures, cancellation, recovery, and responsive UI.
- [ ] Turn optional network features off. Observe zero requests for the disabled
  feature over its polling interval and across restart; test a cold start and
  an in-flight toggle-off. Verify translation/status/update toggles separately;
  disabling one feature must not be claimed to disable intentional CLI traffic.
- [ ] Exercise automatic update opt-in/off and exact release eligibility. Reject
  wrong product, runtime, tag/version/name, manifest/hash/digest, oversized ZIP,
  traversal, unexpected entries and link/reparse candidates without applying.
- [ ] In a disposable install with synthetic state, interrupt update before and
  after replacement, deny file access, simulate a new runtime that never becomes
  ready, and restart. Confirm rollback restores the previous working widget,
  preserves recoverable backups, and neither restarts nor modifies Codex.
- [ ] Review compact and expanded layouts in English and Chinese at 100%, 125%,
  150% and 200% DPI, including mixed-monitor movement, long text, countdown,
  tooltips, keyboard access, tray and missing-data states. No clipped controls.
- [ ] Unsigned Windows limitation accepted: these binaries are not Authenticode
  signed. SmartScreen/Defender/browser warnings or blocking can occur, and no
  reputation or warning-free installation is promised. Do not disable security
  protection as an acceptance workaround; document a signing plan if required.

## Draft Candidate Procedure

1. Merge reviewed source, tests and workflows into main. Never release directly
   from an unreviewed feature branch and never auto-merge to make a release pass.
2. Complete every automated and real-machine gate for the exact main SHA; put
   evidence and independent sign-off in a same-repository GitHub issue. Any main
   change requires rerunning gates and reapproval, even if BuildVersion is equal.
3. A repository administrator must first configure environment `release-candidate`
   with required reviewers, prevent self-review, disable administrator bypass,
   and restrict deployments to main. Workflow YAML alone does not configure these
   protections. Required-reviewer availability depends on repository visibility
   and GitHub plan; if unavailable, **stop** rather than silently bypass approval.
4. Manually dispatch `release.yml` on main, supplying the exact BuildVersion,
   acceptance issue URL and explicit completed-acceptance confirmation. Reviewers
   approve the protected environment only after checking that issue and exact SHA.
5. The workflow reruns read-only gates and audited build, requires a successful
   push CI run for that SHA, rechecks main HEAD, and refuses any existing tag.
   Its trusted-main job uses only `github.token` to create a **draft prerelease**
   with the exact ZIP and `SHA256SUMS.txt`. No PAT, PR token or new upload action.
   Main advancing during approval/build requires a new dispatch, not a forced tag.
6. Inspect the draft assets/checksums, target commit, acceptance evidence and
   resulting GitHub asset digest manually. Partial/failed drafts require manual
   inspection; the workflow never overwrites assets, merges, pushes or publishes.
   A final, separately authorized human review may later publish and mark stable;
   this workflow never clears draft/prerelease or selects a stable latest release.

Do not dispatch this workflow merely to test it: draft creation/uploads are remote
writes. Local script/static results do not establish that GitHub-hosted Windows CI,
environment protection, permissions, real accounts or update rollback have passed.

References: [GitHub environment protections](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments),
[GitHub CLI draft release options](https://cli.github.com/manual/gh_release_create).
