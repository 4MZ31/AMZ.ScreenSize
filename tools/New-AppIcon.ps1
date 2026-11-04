param([string]$OutputPath = (Join-Path $PSScriptRoot '..\assets\app.ico'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition 'public static class AppIconNative { [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool DestroyIcon(System.IntPtr icon); }'
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputPath))) | Out-Null
$bitmap = New-Object Drawing.Bitmap 64,64
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$ink = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(29,43,53))
$accent = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(46,107,120))
$light = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(130,210,191))
try {
    $graphics.Clear([Drawing.Color]::Transparent)
    $graphics.FillRectangle($ink,2,8,60,44)
    $graphics.FillRectangle($accent,8,14,48,32)
    $graphics.FillRectangle($light,16,20,32,20)
    $graphics.FillRectangle($ink,28,52,8,7)
    $graphics.FillRectangle($ink,18,59,28,4)
    $handle = $bitmap.GetHicon()
    try {
        $icon = [Drawing.Icon]::FromHandle($handle)
        $stream = [IO.File]::Create([IO.Path]::GetFullPath($OutputPath))
        try { $icon.Save($stream) } finally { $stream.Dispose() }
    } finally { [AppIconNative]::DestroyIcon($handle) | Out-Null }
} finally { $graphics.Dispose(); $bitmap.Dispose(); $ink.Dispose(); $accent.Dispose(); $light.Dispose() }
