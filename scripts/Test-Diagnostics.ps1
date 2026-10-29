param([Parameter(Mandatory = $true)][string]$PublishPath)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = [IO.Path]::GetFullPath($PublishPath)
$fixture = Join-Path $root ('bin/diagnostic-test-' + [Guid]::NewGuid().ToString('N'))
$payload = @('CodexUsageWidget.dll', 'CodexUsageWidget.deps.json', 'CodexUsageWidget.runtimeconfig.json')
$versionSource = [IO.File]::ReadAllText((Join-Path $root 'WidgetRuntimeStatus.cs'))
$versions = [regex]::Matches($versionSource, 'BuildVersion = "([0-9]{4}\.[0-9]{2}\.[0-9]{2}\.[0-9]{1,4})"')
if ($versions.Count -ne 1) { throw 'Missing or ambiguous source build version.' }
$version = $versions[0].Groups[1].Value
. (Join-Path $PSScriptRoot 'Assert-RuntimeContract.ps1')
Assert-WidgetRuntimeContract (Join-Path $source 'CodexUsageWidget.runtimeconfig.json')
New-Item -ItemType Directory -Path $fixture | Out-Null
foreach ($name in $payload) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $fixture $name)
}
$dotnet = (Get-Command dotnet.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$info = New-Object Diagnostics.ProcessStartInfo
$info.FileName = $dotnet
$info.Arguments = '"' + (Join-Path $fixture 'CodexUsageWidget.dll') + '" --diagnose-build'
$info.WorkingDirectory = $fixture
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
# Replace only the child's environment; never inherit credential-bearing variables
# or read settings/auth files. The parent/global environment stays unchanged.
try { $environment = $info.get_EnvironmentVariables() }
catch {
    if ($_.Exception.InnerException -isnot [ArgumentException]) { throw }
    $environment = $info.get_EnvironmentVariables()
}
$environment.Clear()
$environment['SystemRoot'] = $env:SystemRoot
$environment['WINDIR'] = $env:WINDIR
$environment['PATH'] = (Split-Path -Parent $dotnet) + ';' + (Join-Path $env:SystemRoot 'System32')
$environment['DOTNET_ROOT'] = Split-Path -Parent $dotnet
$environment['DOTNET_SKIP_FIRST_TIME_EXPERIENCE'] = '1'
$environment['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
$environment['DOTNET_NOLOGO'] = '1'
foreach ($name in @('HOME', 'USERPROFILE', 'APPDATA', 'LOCALAPPDATA', 'CODEX_HOME', 'DOTNET_CLI_HOME', 'TEMP', 'TMP', 'XDG_CONFIG_HOME', 'XDG_DATA_HOME')) {
    $environment[$name] = Join-Path $fixture ('isolated-' + $name)
}
$before = @{}
foreach ($name in $payload) { $before[$name] = (Get-FileHash -LiteralPath (Join-Path $fixture $name) -Algorithm SHA256).Hash }
$process = [Diagnostics.Process]::Start($info)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(20000)) {
        $process.Kill()
        $process.WaitForExit()
        throw 'Build diagnostic did not exit within 20 seconds.'
    }
    $output = $stdout.GetAwaiter().GetResult()
    $errorOutput = $stderr.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0 -or -not [string]::IsNullOrWhiteSpace($errorOutput)) { throw 'Build diagnostic failed or wrote stderr.' }
    # An exited host's cached window handle does not identify a WPF window.
    # This fixture checks process/output/files only, not real GUI visibility.
    $report = $output | ConvertFrom-Json
    if ($null -eq $report -or $report.product -cne 'mali-yaya' -or $report.version -cne $version -or
        $report.platform -cne 'win-x64' -or $report.runtimeMajor -ne 10) { throw 'Build diagnostic identity/runtime mismatch.' }
    $fields = @($report.PSObject.Properties.Name)
    $allowed = @('product', 'version', 'platform', 'runtimeMajor')
    if ($fields.Count -ne $allowed.Count -or @($fields | Where-Object { $_ -cnotin $allowed }).Count) { throw 'Unexpected diagnostic fields.' }
    $actual = @(Get-ChildItem -LiteralPath $fixture -Force -File | Select-Object -ExpandProperty Name)
    if ($actual.Count -ne $payload.Count -or @($actual | Where-Object { $_ -notin $payload }).Count) {
        throw 'Diagnostic unexpectedly created runtime or cache files.'
    }
    if (@(Get-ChildItem -LiteralPath $fixture -Force -Directory).Count) {
        throw 'Diagnostic unexpectedly created a directory.'
    }
    foreach ($name in $payload) {
        if ((Get-FileHash -LiteralPath (Join-Path $fixture $name) -Algorithm SHA256).Hash -cne $before[$name]) { throw 'Build diagnostic modified its payload.' }
    }
    Write-Host 'PASS: isolated build diagnostic exits successfully with .NET 10 identity JSON; fixture inventory and payload unchanged.'
    Write-Host 'NOT VERIFIED: real GUI visibility/Show and outbound network behavior; require separate acceptance.'
}
finally { $process.Dispose() }
