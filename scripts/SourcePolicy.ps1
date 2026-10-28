$ErrorActionPreference = 'Stop'

function Get-WidgetSourceEntries([string]$Root) {
    $fixed = @(
        '.gitignore', '.gitattributes', 'app.manifest', 'App.xaml', 'App.xaml.cs', 'AssemblyInfo.cs',
        'build.ps1', 'CodexRateLimitClient.cs', 'CodexExecutableResolver.cs', 'CodexConnectionSettings.cs',
        'CodexUsageWidget.csproj', 'Directory.Build.props',
        'FiveHourCountdownPolicy.cs', 'FlipNumber.xaml', 'FlipNumber.xaml.cs',
        'MainWindow.xaml', 'MainWindow.xaml.cs', 'Models.cs',
        'restart-widget.ps1', 'start-widget.cmd', 'start-widget.ps1',
        'StartupManager.cs', 'TiboChineseBriefClient.cs', 'TiboPollingPolicy.cs',
        'TiboResetMonitor.cs', 'TiboStatusCoordinator.cs', 'UsageAgent.cs',
        'UsageHintPolicy.cs', 'UsageWindowSelector.cs', 'verify-widget.ps1',
        'WidgetLayoutPolicy.cs', 'WidgetRuntimeStatus.cs', 'WidgetUpdateService.cs', 'WidgetUpdateSettings.cs', 'WidgetUpdateInstaller.cs', 'UiText.cs', 'README.github.md',
        'Assets/mali-yaya.png', 'Assets/mali-yaya.ico',
        'RIGHTS.md', 'PRIVACY.md', 'SECURITY.md', 'THIRD_PARTY.md', 'README.en.md',
        'launcher/WidgetLauncher.cs', 'launcher/RestartWidget.cs', 'launcher/InstallDesktopEntry.cs',
        'scripts/SourcePolicy.ps1', 'scripts/Prepare-Source.ps1',
        'scripts/Verify-Source.ps1', 'scripts/Test-SourcePackaging.ps1', 'scripts/Test-Launcher.ps1', 'scripts/Test-Diagnostics.ps1', 'scripts/Prepare-Release.ps1', 'scripts/Test-ReleasePackage.ps1', 'scripts/Build-Icon.ps1', 'scripts/Test-Icon.ps1',
        'docs/PUBLISHING.md', 'docs/AUTO-DISCOVERY.md', 'docs/AUTO-UPDATE.md', 'docs/MASCOT-DESIGN.md', 'docs/ICON-DESIGN.md', '.github/workflows/verify.yml'
    )
    foreach ($relative in $fixed) {
        $source = Join-Path $Root $relative
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Required source file missing: $relative"
        }
        [pscustomobject]@{ Source = $source; Path = $relative }
    }
    # Never export the workstation-specific README.
    [pscustomobject]@{ Source = (Join-Path $Root 'README.github.md'); Path = 'README.md' }
    $tests = Join-Path $Root 'tests/CodexUsageWidget.Tests'
    foreach ($file in Get-ChildItem -LiteralPath $tests -File) {
        if ($file.Extension -in @('.cs', '.csproj')) {
            [pscustomobject]@{
                Source = $file.FullName
                Path = 'tests/CodexUsageWidget.Tests/' + $file.Name
            }
        }
    }
}

function Assert-WidgetSourceText([string]$Path, [string]$Label) {
    # Only these reviewed artwork binaries may bypass text scanning, with pinned hashes.
    $artwork = @{
        'Assets/mali-yaya.png' = '2800b23ec6f6dea9a94f9d5351637639c0eb3eccb55f3a90855e6f1b36ba9eec'
        'Assets/mali-yaya.ico' = '4c03ec2a9d49defebe73a322ec837d1a3c22780a6857d28c4e3a223a989fc999'
    }
    if ($artwork.ContainsKey($Label)) {
        if ((Get-Item -LiteralPath $Path).Length -gt 2MB -or
            (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ine $artwork[$Label]) {
            throw "Artwork fingerprint mismatch: $Label"
        }
        return
    }
    if ((Get-Item -LiteralPath $Path).Length -gt 2MB) {
        throw "Source file exceeds 2 MiB: $Label"
    }
    $text = [IO.File]::ReadAllText($Path)
    $checks = @{
        'GitHub token' = '\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})'
        'OpenAI-style key' = '\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{24,}'
        'private key' = '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
        'workstation user path' = '[A-Za-z]:[\\/]Users[\\/][^\\/\r\n\s]+[\\/]'
    }
    foreach ($kind in $checks.Keys) {
        if ([regex]::IsMatch($text, $checks[$kind])) {
            # Report only the category/path, never the matching secret.
            throw "Source scan rejected $Label ($kind)."
        }
    }
}
