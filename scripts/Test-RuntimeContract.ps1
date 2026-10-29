$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assert-RuntimeContract.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture = Join-Path $root ('bin/runtime-contract-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$utf8 = New-Object Text.UTF8Encoding($false)
$script:scenarios = 0

function New-Contract {
    return @{ runtimeOptions = @{ tfm = 'net10.0'; frameworks = @(
        @{ name = 'Microsoft.NETCore.App'; version = '10.0.0' },
        @{ name = 'Microsoft.WindowsDesktop.App'; version = '10.0.1' }
    ) } }
}

function Test-Contract([string]$Name, [string]$Json, [bool]$Accept) {
    $path = Join-Path $fixture ($Name + '.runtimeconfig.json')
    [IO.File]::WriteAllText($path, $Json, $utf8)
    $before = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    $accepted = $true
    try { Assert-WidgetRuntimeContract $path }
    catch { $accepted = $false }
    if ($accepted -ne $Accept) { throw ('Runtime contract fixture failed: ' + $Name + ', expected acceptance=' + $Accept) }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $before) { throw 'Validator modified a fixture.' }
    $script:scenarios++
}

function Test-Object([string]$Name, $Value, [bool]$Accept) {
    Test-Contract $Name ($Value | ConvertTo-Json -Depth 12 -Compress) $Accept
}

Test-Object 'valid-net10' (New-Contract) $true
foreach ($roll in @('Disable', 'LatestPatch', 'Minor', 'LatestMinor')) {
    $config = New-Contract
    $config.runtimeOptions.rollForward = $roll
    Test-Object ('valid-' + $roll) $config $true
}
foreach ($roll in @('Major', 'LatestMajor', 'latestpatch', '', 10)) {
    $config = New-Contract
    $config.runtimeOptions.rollForward = $roll
    Test-Object ('bad-roll-' + [string]$script:scenarios) $config $false
}
foreach ($tfm in @('net9.0', 'net11.0', 'net10.0-windows', '', 'NET10.0')) {
    $config = New-Contract
    $config.runtimeOptions.tfm = $tfm
    Test-Object ('bad-tfm-' + [string]$script:scenarios) $config $false
}
foreach ($version in @('9.0.9', '11.0.0', '10.1.0', '10.0.01', '10.0.0-preview.1', '10.0.0.1', '../10.0.0', '10.0.2147483648')) {
    $config = New-Contract
    $config.runtimeOptions.frameworks[0].version = $version
    Test-Object ('bad-version-' + [string]$script:scenarios) $config $false
}
foreach ($name in @('Microsoft.AspNetCore.App', 'microsoft.netcore.app', '../Microsoft.NETCore.App')) {
    $config = New-Contract
    $config.runtimeOptions.frameworks[0].name = $name
    Test-Object ('bad-framework-' + [string]$script:scenarios) $config $false
}
$config = New-Contract
$config.runtimeOptions.frameworks[1].name = 'Microsoft.NETCore.App'
Test-Object 'duplicate-framework' $config $false
$config = New-Contract
$config.runtimeOptions.frameworks = @($config.runtimeOptions.frameworks[0])
Test-Object 'missing-desktop-framework' $config $false
$config = New-Contract
$config.runtimeOptions.frameworks += @{ name = 'Microsoft.AspNetCore.App'; version = '10.0.0' }
Test-Object 'extra-framework' $config $false
$config = New-Contract
$config.runtimeOptions.frameworks[0].path = '../unexpected'
Test-Object 'framework-path-injection' $config $false
foreach ($property in @('framework', 'includedFrameworks', 'additionalProbingPaths', 'rollForwardOnNoCandidateFx')) {
    $config = New-Contract
    $config.runtimeOptions[$property] = @('../never-probe-this-fixture')
    Test-Object ('forbidden-' + $property) $config $false
}
Test-Contract 'invalid-json' '{' $false
Test-Contract 'missing-options' '{}' $false
Test-Contract 'null-config' 'null' $false
$valid = (New-Contract) | ConvertTo-Json -Depth 12 -Compress
Test-Contract 'exact-32k' ($valid + (' ' * (32KB - $utf8.GetByteCount($valid)))) $true
Test-Contract 'oversize-32k-plus-one' ($valid + (' ' * (32KB + 1 - $utf8.GetByteCount($valid)))) $false
foreach ($path in @((Join-Path $fixture 'missing.runtimeconfig.json'), $fixture)) {
    $accepted = $true
    try { Assert-WidgetRuntimeContract $path }
    catch { $accepted = $false }
    if ($accepted) { throw 'Missing-file/directory path unexpectedly accepted.' }
    $script:scenarios++
}
Write-Host "PASS: $script:scenarios synthetic runtime-contract fixtures; no downloads, runtime execution, or account file access."
