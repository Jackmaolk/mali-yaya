# Third-party components and services

The application currently has no third-party NuGet PackageReference. It targets
Microsoft .NET / WPF and relies on a separately installed Codex executable.
No Codex binaries, user authentication files or .NET redistributables are included
in the source export.

The test project declares Microsoft.NET.Test.Sdk 17.14.1, xunit 2.9.3 and
xunit.runner.visualstudio 3.1.4. Transitive packages include Microsoft test platform
components, Newtonsoft.Json 13.0.3, System.Collections.Immutable 8.0.0,
System.Reflection.Metadata 8.0.0 and xunit components. See each restored package's
NuGet metadata for its applicable license and notices; do not assume all packages
use the same license. Preserve required notices if redistributing any dependencies.

Local restored NuGet metadata declares MIT for the Microsoft test platform packages,
Newtonsoft.Json, System.Collections.Immutable and System.Reflection.Metadata;
Apache-2.0 for xunit 2.9.3, xunit.analyzers 1.18.0 and the Visual Studio runner 3.1.4.
xunit.abstractions 2.0.3 links to the xunit project's license file rather than a
license expression, so its applicable historical terms still need explicit review
before redistributing that test dependency. Test packages are not shipped by the
current application build.

External services (not bundled code):

- GitHub Releases API and asset hosting, used only after opting into updates for an
  explicitly configured owned repository. SHA-256 metadata is not an independent
  publisher signature. No GitHub credentials are stored by this updater.

- FxEmbed: <https://github.com/FxEmbed/FxEmbed>, MIT-licensed source. This does not
  grant rights in X posts or guarantee the availability/terms of its hosted service.
- Community monitor: <https://codex.gussuriworks.com/api/current>. No hosted-service
  SLA, authorization for commercial redistribution, or completeness guarantee has
  been verified in this review.
- X/Tibo: linked public posts remain content of their respective rights holders.

The current WPF leaf robot is newly drawn in this project's XAML, without downloaded
character artwork. Earlier private snapshots can contain the previous character;
do not redistribute them as the new release. See RIGHTS.md and docs/MASCOT-DESIGN.md.
No legal clearance, trademark registration or blanket public/commercial license is
asserted by this change.

The app icon PNG was newly generated with the built-in image_gen tool for this
project, then mechanically converted into a multi-size Windows ICO. It is not a
downloaded franchise image. Generation prompt and provenance are recorded in
docs/ICON-DESIGN.md. This does not assert trademark registration or legal clearance.

GitHub Actions, when manually requested, uses the official checkout and setup-dotnet
actions pinned to exact commits. They are not included in the shipped application.

README organization and release/digest update design were informed by
https://github.com/JiaYang-BUAA/Codex-Desktop-Usage-Monitor-Windows (MIT notice
observed). No code, screenshots, CDP injection or Node runtime from that project
were copied into this application; its license does not license this entire project.
