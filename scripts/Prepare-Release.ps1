param(
    [Parameter(Mandatory = $true)][string]$PublishPath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$Version
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '\A[0-9]{4}\.[0-9]{2}\.[0-9]{2}\.[0-9]{1,4}\z') { throw 'Expected YYYY.MM.DD.revision version.' }
$source = [IO.Path]::GetFullPath($PublishPath)
. (Join-Path $PSScriptRoot 'Assert-RuntimeContract.ps1')
Assert-WidgetRuntimeContract (Join-Path $source 'CodexUsageWidget.runtimeconfig.json')
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $destination) { throw 'Output directory must be new.' }
$names = @('CodexUsageWidget.dll', 'CodexUsageWidget.exe', 'CodexUsageWidget.deps.json', 'CodexUsageWidget.runtimeconfig.json')
$expandedSize = 0L
foreach ($name in $names) {
    $file = Join-Path $source $name
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -le 0) { throw ('Missing runtime file: ' + $name) }
    $item = Get-Item -LiteralPath $file
    if ($item.Length -gt 20MB -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Runtime file exceeds bounds or is a link.' }
    $expandedSize += $item.Length
}
if ($expandedSize -gt (40MB - 32KB)) { throw 'Expanded runtime exceeds update limit.' }
# Only execute the publisher-supplied local build, with a no-GUI/no-network mode.
$info = New-Object Diagnostics.ProcessStartInfo
$info.FileName = (Get-Command dotnet.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$info.Arguments = '"' + (Join-Path $source 'CodexUsageWidget.dll') + '" --diagnose-build'
$info.WorkingDirectory = $source
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
$process = [Diagnostics.Process]::Start($info)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(10000)) { $process.Kill(); throw 'Build diagnostic timed out.' }
    $report = $stdout.GetAwaiter().GetResult() | ConvertFrom-Json
    [void]$stderr.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0 -or $report.product -ne 'mali-yaya' -or $report.version -ne $Version -or
        $report.platform -ne 'win-x64' -or $report.runtimeMajor -ne 10) { throw 'Build identity/version mismatch.' }
}
finally { $process.Dispose() }
$parent = Split-Path -Parent $destination
if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent | Out-Null }
$zip = $destination + '.zip'
if (Test-Path -LiteralPath $zip) { throw 'Archive already exists.' }
$expectedName = 'mali-yaya-' + $Version + '-windows-x64'
if ((Split-Path -Leaf $destination) -cne $expectedName) { throw ('Output directory name must be ' + $expectedName) }
New-Item -ItemType Directory -Path $destination | Out-Null
$hashes = [ordered]@{}
foreach ($name in $names) {
    $target = Join-Path $destination $name
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination $target
    $hashes[$name] = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
}
$manifest = [ordered]@{ product = 'mali-yaya'; version = $Version; platform = 'win-x64'; runtimeMajor = 10; files = $hashes }
[IO.File]::WriteAllText((Join-Path $destination 'release-manifest.json'), ($manifest | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding($false)))
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($destination, $zip)
if ((Get-Item -LiteralPath $zip).Length -gt 20MB) { throw 'Release ZIP exceeds updater limit.' }
Write-Host ('Prepared release asset: ' + $zip)
Write-Host ('SHA256: ' + (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash)
Write-Host ('Required GitHub tag: v' + $Version + '. Upload as a formal Release; verify GitHub asset digest before enabling updates.')
