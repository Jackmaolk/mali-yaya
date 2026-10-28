param([string]$Source, [string]$Output)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Source) { $Source = Join-Path $root 'Assets/mali-yaya.png' }
if (-not $Output) { $Output = Join-Path $root 'Assets/mali-yaya.ico' }
Add-Type -AssemblyName System.Drawing
$sourceImage = [Drawing.Image]::FromFile([IO.Path]::GetFullPath($Source))
$frames = @()
try {
    if ($sourceImage.Width -ne $sourceImage.Height) { throw 'Icon source must be square.' }
    foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
        $bitmap = New-Object Drawing.Bitmap($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $stream = New-Object IO.MemoryStream
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($sourceImage, (New-Object Drawing.Rectangle(0, 0, $size, $size)))
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $frames += [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
        }
        finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
}
finally { $sourceImage.Dispose() }
$file = [IO.File]::Create([IO.Path]::GetFullPath($Output))
$writer = New-Object IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = [byte]$(if ($frame.Size -eq 256) { 0 } else { $frame.Size })
        $writer.Write($dimension); $writer.Write($dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
}
finally { $writer.Dispose(); $file.Dispose() }
Write-Host 'Built 9 PNG-in-ICO sizes (16 through 256 pixels).'
