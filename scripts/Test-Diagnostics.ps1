param([Parameter(Mandatory = $true)][string]$PublishPath)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = [IO.Path]::GetFullPath($PublishPath)
$fixture = Join-Path $root ('bin/diagnostic-test-' + [Guid]::NewGuid().ToString('N'))
$payload = @('CodexUsageWidget.dll', 'CodexUsageWidget.deps.json', 'CodexUsageWidget.runtimeconfig.json')
New-Item -ItemType Directory -Path $fixture | Out-Null
foreach ($name in $payload) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $fixture $name)
}
$dotnet = (Get-Command dotnet.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$info = New-Object Diagnostics.ProcessStartInfo
$info.FileName = $dotnet
$info.Arguments = '"' + (Join-Path $fixture 'CodexUsageWidget.dll') + '" --diagnose-cli'
$info.WorkingDirectory = $fixture
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
# Read-only path/version detection may succeed or report no installation; neither
# result should start the UI or write runtime data. Do not change configuration.
$process = [Diagnostics.Process]::Start($info)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(20000)) {
        $process.Kill()
        throw 'Diagnostic process did not exit within 20 seconds.'
    }
    $output = $stdout.GetAwaiter().GetResult()
    [void]$stderr.GetAwaiter().GetResult()
    if ($process.ExitCode -notin @(0, 1)) { throw 'Unexpected diagnostic exit code.' }
    $report = $output | ConvertFrom-Json
    if ($null -eq $report) { throw 'Diagnostic did not return a JSON report.' }
    $actual = @(Get-ChildItem -LiteralPath $fixture -Force -File | Select-Object -ExpandProperty Name)
    if ($actual.Count -ne $payload.Count -or @($actual | Where-Object { $_ -notin $payload }).Count) {
        throw 'Diagnostic unexpectedly created runtime or cache files.'
    }
    if (@(Get-ChildItem -LiteralPath $fixture -Force -Directory).Count) {
        throw 'Diagnostic unexpectedly created a directory.'
    }
    Write-Host 'PASS: diagnostic exits with JSON and does not create window runtime/cache files.'
}
finally { $process.Dispose() }
