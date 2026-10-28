param([string]$Launcher)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$icon = Join-Path $root 'Assets/mali-yaya.ico'
$bytes = [IO.File]::ReadAllBytes($icon)
if ([BitConverter]::ToUInt16($bytes, 0) -ne 0 -or [BitConverter]::ToUInt16($bytes, 2) -ne 1 -or
    [BitConverter]::ToUInt16($bytes, 4) -ne 9) { throw 'Invalid ICO header or frame count.' }
Add-Type -AssemblyName System.Drawing
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
for ($index = 0; $index -lt $sizes.Count; $index++) {
    $entry = 6 + 16 * $index
    $size = $sizes[$index]
    $dimension = $(if ($size -eq 256) { 0 } else { $size })
    if ($bytes[$entry] -ne $dimension -or $bytes[$entry + 1] -ne $dimension) { throw 'Wrong ICO dimensions.' }
    $length = [BitConverter]::ToUInt32($bytes, $entry + 8)
    $offset = [BitConverter]::ToUInt32($bytes, $entry + 12)
    if ($offset -lt 150 -or $length -le 8 -or $offset + $length -gt $bytes.Length) { throw 'Invalid ICO bounds.' }
    $memory = New-Object IO.MemoryStream(,$bytes[$offset..($offset + $length - 1)])
    $image = [Drawing.Bitmap]::FromStream($memory)
    try {
        if ($image.Width -ne $size -or $image.Height -ne $size) { throw 'Wrong PNG frame size.' }
        if ($image.GetPixel([int]($size / 2), [int]($size / 2)).A -lt 240) { throw 'Icon center is not opaque.' }
        if ($image.GetPixel(0, 0).A -gt 32) { throw 'Icon outer corner is not transparent.' }
    }
    finally { $image.Dispose(); $memory.Dispose() }
}
if ($Launcher) {
    $embedded = [Drawing.Icon]::ExtractAssociatedIcon([IO.Path]::GetFullPath($Launcher))
    if ($null -eq $embedded) { throw 'Launcher does not expose an associated icon.' }
    $bitmap = $embedded.ToBitmap()
    $reference = $null
    $referenceStream = $null
    try {
        $frameIndex = [Array]::IndexOf($sizes, $bitmap.Width)
        if ($frameIndex -lt 0 -or $bitmap.Width -ne $bitmap.Height) { throw 'Unexpected shell icon size.' }
        $entry = 6 + 16 * $frameIndex
        $length = [BitConverter]::ToUInt32($bytes, $entry + 8)
        $offset = [BitConverter]::ToUInt32($bytes, $entry + 12)
        $referenceStream = New-Object IO.MemoryStream(,$bytes[$offset..($offset + $length - 1)])
        $reference = [Drawing.Bitmap]::FromStream($referenceStream)
        $samples = 0
        for ($x = 0; $x -lt $bitmap.Width; $x++) {
            for ($y = 0; $y -lt $bitmap.Height; $y++) {
                $actual = $bitmap.GetPixel($x, $y)
                $expected = $reference.GetPixel($x, $y)
                if ($expected.A -lt 240) { continue }
                if ($actual.A -lt 230 -or [Math]::Abs([int]$actual.R - $expected.R) -gt 5 -or
                    [Math]::Abs([int]$actual.G - $expected.G) -gt 5 -or [Math]::Abs([int]$actual.B - $expected.B) -gt 5) {
                    throw 'Embedded launcher icon differs from the reviewed ICO frame.'
                }
                $samples++
            }
        }
        if ($samples -lt 100) { throw 'Too few opaque pixels verified in the shell icon.' }
    }
    finally {
        if ($reference) { $reference.Dispose() }; if ($referenceStream) { $referenceStream.Dispose() }
        $bitmap.Dispose(); $embedded.Dispose()
    }
}
Write-Host 'PASS: 9 icon sizes, PNG payloads, alpha and optional embedded launcher icon.'
