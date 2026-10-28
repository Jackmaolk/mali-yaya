# Security

This is a private review snapshot, not a supported public binary distribution.

Report suspected problems privately to the repository owner. Do not paste tokens,
Codex authentication/configuration files, conversation history, full runtime state,
or unredacted logs into issues. Old restart logs can contain account usage data.

The source export uses an explicit file allowlist, SHA256 inventory and limited
high-confidence secret/path checks. These checks reduce accidental disclosure;
they do not detect every secret or establish that the software is vulnerability-free.

Review external HTTP boundaries, process ownership, startup registration and local
state permissions before distributing binaries. Do not disable certificate validation
or run the widget as administrator to work around connectivity problems.

Dependencies and .NET require ongoing security updates. A failed/unavailable package
audit must be reported as incomplete, never as a clean audit. The manual CI workflow
treats NuGet vulnerability findings and audit-data failures as errors.

Public releases require verified artwork rights, an explicit code license, a portable
installation path, a supported runtime, and a tested upgrade/uninstall strategy.
