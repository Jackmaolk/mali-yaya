$ErrorActionPreference = 'Stop'

function Get-WidgetSourceEntries([string]$Root) {
    $fixed = @(
        '.gitignore', '.gitattributes', 'app.manifest', 'App.xaml', 'App.xaml.cs', 'AssemblyInfo.cs',
        'build.ps1', 'CodexRateLimitClient.cs', 'CodexExecutableResolver.cs', 'CodexConnectionSettings.cs',
        'CodexUsageWidget.csproj', 'Directory.Build.props', 'global.json',
        'FiveHourCountdownPolicy.cs', 'FlipNumber.xaml', 'FlipNumber.xaml.cs',
        'MainWindow.xaml', 'MainWindow.xaml.cs', 'Models.cs',
        'restart-widget.ps1', 'start-widget.cmd', 'start-widget.ps1',
        'StartupManager.cs', 'TiboChineseBriefClient.cs', 'TiboPollingPolicy.cs',
        'TiboResetMonitor.cs', 'TiboStatusCoordinator.cs', 'UsageAgent.cs',
        'UsageHintPolicy.cs', 'UsageWindowSelector.cs', 'verify-widget.ps1',
        'WidgetLayoutPolicy.cs', 'WidgetRuntimeStatus.cs', 'WidgetDataPaths.cs', 'WidgetPreferences.cs', 'WidgetUpdateService.cs', 'WidgetUpdateSettings.cs', 'WidgetUpdateInstaller.cs', 'UiText.cs', 'README.github.md',
        'Assets/mali-yaya.png', 'Assets/mali-yaya.ico',
        'docs/images/widget-reset-complete-zh.png',
        'docs/images/widget-confirmed-en.png',
        'docs/images/widget-confirmed-zh.png',
        'docs/images/widget-low-en.png',
        'docs/images/widget-low-zh.png',
        'docs/images/widget-minimal-en.png',
        'docs/images/widget-minimal-zh.png',
        'docs/images/widget-normal-en.png',
        'docs/images/widget-normal-zh.png',
        'docs/images/widget-offline-en.png',
        'docs/images/widget-offline-zh.png',
        'docs/images/widget-planned-en.png',
        'docs/images/widget-planned-zh.png',
        'RIGHTS.md', 'PRIVACY.md', 'SECURITY.md', 'THIRD_PARTY.md', 'README.en.md', 'CHANGELOG.md',
        'launcher/WidgetLauncher.cs', 'launcher/RestartWidget.cs', 'launcher/InstallDesktopEntry.cs',
        'scripts/SourcePolicy.ps1', 'scripts/Prepare-Source.ps1',
        'scripts/Verify-Source.ps1', 'scripts/Test-SourcePackaging.ps1', 'scripts/Test-Launcher.ps1', 'scripts/Test-Diagnostics.ps1', 'scripts/Prepare-Release.ps1', 'scripts/Test-ReleasePackage.ps1', 'scripts/Build-Icon.ps1', 'scripts/Test-Icon.ps1',
        'scripts/Assert-RuntimeContract.ps1', 'scripts/Test-RuntimeContract.ps1',
        'docs/PUBLISHING.md', 'docs/AUTO-DISCOVERY.md', 'docs/AUTO-UPDATE.md', 'docs/MASCOT-DESIGN.md', 'docs/ICON-DESIGN.md',
        'docs/UPGRADE-PLAN.md', 'docs/UPGRADE-VERIFICATION.md', 'docs/RELEASE-ACCEPTANCE.md', '.github/workflows/verify.yml', '.github/workflows/release.yml'
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
    # Only reviewed artwork and this consented screenshot bypass text scanning, with pinned hashes.
    $artwork = @{
        'docs/images/widget-confirmed-en.png' = 'e73902316d24e0d206cbb5817ffd3cc7b5183d5908e34866b116e6c31ed4816c'
        'docs/images/widget-confirmed-zh.png' = '4514523f71cb359657d6d807397104f6b60933d2fe3db701581166e714bae89b'
        'docs/images/widget-low-en.png' = '3becb4fbdcf69d517b23a93b10d3c5eda4ab45b5f2460c293e31cd085856c4c4'
        'docs/images/widget-low-zh.png' = '2c853c2cfaa738f82d8da1b2ec4cb74afd4c3080e1eead8bf3f2442eaaf903e4'
        'docs/images/widget-minimal-en.png' = 'c4fa84ce9480ac21cc49176655965282def84182d1d2c8c9eb1edc237635a222'
        'docs/images/widget-minimal-zh.png' = '8dae35aa0bd8623b998cbee4e9f460bf4dbb0e022dbdb676fb574d79213952e0'
        'docs/images/widget-normal-en.png' = 'b38b3009a43040b54c7674df87e26be84636f8d38e57660fc99dd1ad0387b87f'
        'docs/images/widget-normal-zh.png' = '97d99bd7bdc646512d6f7eaadeb92c7abbfb13ab3479901f774a37c70a34eb87'
        'docs/images/widget-offline-en.png' = 'dc49f55f1cd8a076cd812ee750489380c3e839ab296d7e54ef9f754484757d3b'
        'docs/images/widget-offline-zh.png' = '2c14a1841383699e9612b3a08d32b3f35aff3fdfa0f4cbc68dd4e9349c2ac89e'
        'docs/images/widget-planned-en.png' = '698258f49e035c768a4b18fbcadd76093eba09dcf8daa6a46fa633a40f5b29e5'
        'docs/images/widget-planned-zh.png' = 'c048661183cdef3385682780d4addaf20d2f28314306d43a861361a07aae14bf'
        'Assets/mali-yaya.png' = '2800b23ec6f6dea9a94f9d5351637639c0eb3eccb55f3a90855e6f1b36ba9eec'
        'Assets/mali-yaya.ico' = '4c03ec2a9d49defebe73a322ec837d1a3c22780a6857d28c4e3a223a989fc999'
        'docs/images/widget-reset-complete-zh.png' = '633e6e6a4ae56f337c874f14812e4293b7f250b34bd417db231869f5599991b4'
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
