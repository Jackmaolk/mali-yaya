param([string]$Destination)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SourcePolicy.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Destination) {
    $Destination = Join-Path $root ('bin/source-candidate-' + [Guid]::NewGuid().ToString('N'))
}
$Destination = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $Destination) { throw 'Destination must be a new directory.' }
if (Test-Path -LiteralPath ($Destination + '.zip')) { throw 'Archive already exists.' }

$entries = @(Get-WidgetSourceEntries $root)
foreach ($entry in $entries) { Assert-WidgetSourceText $entry.Source $entry.Path }
New-Item -ItemType Directory -Path $Destination | Out-Null
$manifest = @()
foreach ($entry in $entries) {
    $target = Join-Path $Destination $entry.Path
    $parent = Split-Path -Parent $target
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    }
    Copy-Item -LiteralPath $entry.Source -Destination $target
    $manifest += [pscustomobject]@{
        path = $entry.Path
        sha256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$metadata = [ordered]@{
    schemaVersion = 1
    purpose = 'Reviewed source preview; not a production binary release'
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    files = $manifest
}
$json = $metadata | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $Destination 'source-manifest.json'), $json,
    (New-Object Text.UTF8Encoding($false)))
& (Join-Path $PSScriptRoot 'Verify-Source.ps1') -Root $Destination
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($Destination, $Destination + '.zip')
Write-Host "Source directory: $Destination"
Write-Host "Source archive: $Destination.zip"
Write-Host ('Archive SHA256: ' + (Get-FileHash -LiteralPath ($Destination + '.zip')).Hash)
