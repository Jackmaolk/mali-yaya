$ErrorActionPreference = 'Stop'
function Assert-WidgetRuntimeContract([string]$Path) {
    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($item.Length -gt 32KB -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Invalid runtime configuration.' }
    $config = [IO.File]::ReadAllText($Path) | ConvertFrom-Json
    $options = $config.runtimeOptions
    if ($null -eq $options -or $options.tfm -cne 'net10.0' -or @($options.frameworks).Count -ne 2 -or
        $null -ne $options.framework -or $null -ne $options.includedFrameworks -or
        $null -ne $options.additionalProbingPaths -or $null -ne $options.rollForwardOnNoCandidateFx) {
        throw 'Incompatible runtime configuration: expected framework-dependent .NET 10.'
    }
    if ($null -ne $options.rollForward -and $options.rollForward -cnotin @('Disable', 'LatestPatch', 'Minor', 'LatestMinor')) { throw 'Invalid runtime roll-forward.' }
    $names = @()
    foreach ($framework in $options.frameworks) {
        if ($framework.name -cnotin @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App') -or
            $framework.name -cin $names -or @($framework.PSObject.Properties).Count -ne 2 -or
            $framework.version -cnotmatch '\A10\.0\.(0|[1-9][0-9]*)\z') { throw 'Invalid runtime framework.' }
        $parsed = $null
        if (-not [Version]::TryParse($framework.version, [ref]$parsed)) { throw 'Invalid runtime version.' }
        $names += $framework.name
    }
}
