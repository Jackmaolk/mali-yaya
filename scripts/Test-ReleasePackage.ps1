param([Parameter(Mandatory = $true)][string]$PublishPath, [Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path $root ('bin/release-tests-' + [Guid]::NewGuid().ToString('N'))
$destination = Join-Path $testRoot ('mali-yaya-' + $Version + '-windows-x64')
& (Join-Path $PSScriptRoot 'Prepare-Release.ps1') -PublishPath $PublishPath -OutputDirectory $destination -Version $Version
$expected = @('CodexUsageWidget.dll', 'CodexUsageWidget.exe', 'CodexUsageWidget.deps.json', 'CodexUsageWidget.runtimeconfig.json')
$archive = [IO.Compression.ZipFile]::OpenRead($destination + '.zip')
try {
    if ($archive.Entries.Count -ne 5) { throw 'Unexpected release entry count.' }
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName -cnotin ($expected + @('release-manifest.json'))) { throw 'Unexpected release entry name.' }
    }
}
finally { $archive.Dispose() }
$manifest = Get-Content -LiteralPath (Join-Path $destination 'release-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.product -ne 'mali-yaya' -or $manifest.version -ne $Version -or $manifest.runtimeMajor -ne 10) { throw 'Incorrect release identity.' }
foreach ($name in $expected) {
    if ($manifest.files.$name -cne (Get-FileHash -LiteralPath (Join-Path $destination $name)).Hash.ToLowerInvariant()) { throw 'Manifest hash mismatch.' }
}
function Assert-Rejected([string]$Output, [string]$CandidateVersion, [string]$Reason) {
    try {
        & (Join-Path $PSScriptRoot 'Prepare-Release.ps1') -PublishPath $PublishPath -OutputDirectory $Output -Version $CandidateVersion
    }
    catch { if ($_.Exception.Message -like ('*' + $Reason + '*')) { return }; throw }
    throw 'Unsafe package request was not rejected.'
}
Assert-Rejected $destination $Version 'must be new'
$parsed = [Version]$Version
$wrong = '{0:D4}.{1:D2}.{2:D2}.{3}' -f $parsed.Major, $parsed.Minor, $parsed.Build, ($parsed.Revision + 1)
$wrongDestination = Join-Path $testRoot ('mali-yaya-' + $wrong + '-windows-x64')
Assert-Rejected $wrongDestination $wrong 'identity/version mismatch'
if (Test-Path -LiteralPath $wrongDestination) { throw 'Mismatched version unexpectedly created output.' }
Write-Host 'PASS: 3 release package checks (inventory/hash, no overwrite, build version mismatch).'
