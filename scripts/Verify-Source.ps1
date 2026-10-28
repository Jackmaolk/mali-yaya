param([Parameter(Mandatory = $true)][string]$Root)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SourcePolicy.ps1')
$Root = [IO.Path]::GetFullPath($Root)
$expected = @(Get-WidgetSourceEntries $Root | ForEach-Object { $_.Path })
$actual = @(Get-ChildItem -LiteralPath $Root -Recurse -File | ForEach-Object {
    $_.FullName.Substring($Root.TrimEnd('\', '/').Length + 1).Replace('\', '/')
})
foreach ($path in $actual) {
    if ($path -eq 'source-manifest.json') { continue }
    if ($path -notin $expected) { throw "Unexpected file in source export: $path" }
    Assert-WidgetSourceText (Join-Path $Root $path) $path
}
foreach ($path in $expected) {
    if ($path -notin $actual) { throw "Missing exported source: $path" }
}
$manifest = Get-Content -LiteralPath (Join-Path $Root 'source-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or @($manifest.files).Count -ne $expected.Count) {
    throw 'Invalid source manifest.'
}
$seen = @{}
foreach ($entry in $manifest.files) {
    if ($entry.path -notin $expected -or $seen.ContainsKey($entry.path)) {
        throw 'Unknown or duplicate manifest path.'
    }
    $seen[$entry.path] = $true
    $hash = (Get-FileHash -LiteralPath (Join-Path $Root $entry.path) -Algorithm SHA256).Hash
    if ($hash -ine $entry.sha256) { throw "Source hash mismatch: $($entry.path)" }
}
Write-Host "PASS: $($expected.Count) allowlisted source files; hashes and sensitive-data checks passed."
