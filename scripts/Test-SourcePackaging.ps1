$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path $root ('bin/packaging-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$baseline = Join-Path $testRoot 'baseline'
& (Join-Path $PSScriptRoot 'Prepare-Source.ps1') -Destination $baseline

function Assert-Rejected([string]$Folder, [string]$Expected) {
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Verify-Source.ps1') -Root $Folder }
    catch {
        if ($_.Exception.Message -notlike "*$Expected*") { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw "Expected rejection: $Expected" }
}

$leak = Join-Path $testRoot 'runtime-leak'
Copy-Item -LiteralPath $baseline -Destination $leak -Recurse
[IO.File]::WriteAllText((Join-Path $leak 'widget-runtime.json'), '{}')
Assert-Rejected $leak 'Unexpected file'

$tamper = Join-Path $testRoot 'tampered'
Copy-Item -LiteralPath $baseline -Destination $tamper -Recurse
[IO.File]::AppendAllText((Join-Path $tamper 'README.md'), 'changed')
Assert-Rejected $tamper 'hash mismatch'

$secret = Join-Path $testRoot 'secret'
Copy-Item -LiteralPath $baseline -Destination $secret -Recurse
# Synthetic test token; deliberately assembled to avoid storing a token-shaped literal.
[IO.File]::AppendAllText((Join-Path $secret 'README.md'), ('ghp_' + ('A' * 36)))
Assert-Rejected $secret 'GitHub token'

$userPath = Join-Path $testRoot 'personal-path'
Copy-Item -LiteralPath $baseline -Destination $userPath -Recurse
[IO.File]::AppendAllText((Join-Path $userPath 'README.md'), ('C:' + '\Users\Example\Desktop\'))
Assert-Rejected $userPath 'workstation user path'

$zip = [IO.Compression.ZipFile]::OpenRead($baseline + '.zip')
try {
    $manifest = Get-Content -LiteralPath (Join-Path $baseline 'source-manifest.json') -Raw | ConvertFrom-Json
    $expected = @($manifest.files.path) + 'source-manifest.json'
    if ($zip.Entries.Count -ne $expected.Count) { throw 'Archive entry count mismatch.' }
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName.Replace('\', '/') -notin $expected) { throw 'Unexpected archive entry.' }
    }
}
finally { $zip.Dispose() }

$screenshotTamper = Join-Path $testRoot 'screenshot-tamper'
Copy-Item -LiteralPath $baseline -Destination $screenshotTamper -Recurse
[IO.File]::AppendAllText((Join-Path $screenshotTamper 'docs/images/widget-reset-complete-zh.png'), 'tampered')
Assert-Rejected $screenshotTamper 'Artwork fingerprint mismatch'

$unknownScreenshot = Join-Path $testRoot 'unknown-screenshot'
Copy-Item -LiteralPath $baseline -Destination $unknownScreenshot -Recurse
Copy-Item -LiteralPath (Join-Path $unknownScreenshot 'docs/images/widget-reset-complete-zh.png') -Destination (Join-Path $unknownScreenshot 'docs/images/unreviewed.png')
Assert-Rejected $unknownScreenshot 'Unexpected file'

Write-Host 'PASS: 8 packaging checks (clean export, runtime exclusion, tamper, token, personal path, archive inventory, screenshot fingerprint, unreviewed image rejection).'
