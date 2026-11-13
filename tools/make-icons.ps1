#Requires -Version 5.1
<#
.SYNOPSIS
    Generates the tray icon set into src/NaggingTimeTracker.App/Assets/tray/.

.DESCRIPTION
    Each .ico holds 16, 20, 24 and 32 px PNG-compressed images of a filled circle. Grey = idle,
    green = one timer, amber + digit = several timers, red = attention (a nag is showing).
    The ICO container is written by hand because Icon.FromHandle degrades the alpha channel.
    Run with Windows PowerShell 5.1:  powershell -NoProfile -File tools/make-icons.ps1
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path $PSScriptRoot '..\src\NaggingTimeTracker.App\Assets\tray'
$outDir = [System.IO.Path]::GetFullPath($outDir)
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$sizes = 16, 20, 24, 32
$colors = @{
    idle      = [System.Drawing.ColorTranslator]::FromHtml('#8A8A8A')
    tracking  = [System.Drawing.ColorTranslator]::FromHtml('#2E9E44')
    parallel  = [System.Drawing.ColorTranslator]::FromHtml('#E8A000')
    attention = [System.Drawing.ColorTranslator]::FromHtml('#D93025')
}

function New-IconPng([int]$size, [System.Drawing.Color]$color, [string]$label) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $g.Clear([System.Drawing.Color]::Transparent)

        $inset = $size / 32.0
        $brush = New-Object System.Drawing.SolidBrush($color)
        $g.FillEllipse($brush, [single]$inset, [single]$inset, [single]($size - 2 * $inset), [single]($size - 2 * $inset))
        $brush.Dispose()

        if ($label) {
            $factor = if ($label.Length -gt 1) { 0.5 } else { 0.69 }
            $font = New-Object System.Drawing.Font('Segoe UI', [single]($size * $factor), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
            $format = New-Object System.Drawing.StringFormat
            $format.Alignment = [System.Drawing.StringAlignment]::Center
            $format.LineAlignment = [System.Drawing.StringAlignment]::Center
            $rect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
            $g.DrawString($label, $font, [System.Drawing.Brushes]::White, $rect, $format)
            $font.Dispose()
            $format.Dispose()
        }

        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,$ms.ToArray()
    }
    finally {
        $g.Dispose()
        $bmp.Dispose()
    }
}

function Write-Ico([string]$path, [System.Drawing.Color]$color, [string]$label) {
    $images = foreach ($s in $sizes) { ,(New-IconPng $s $color $label) }

    $fs = [System.IO.File]::Create($path)
    $w = New-Object System.IO.BinaryWriter($fs)
    try {
        # ICONDIR
        $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            # ICONDIRENTRY
            $w.Write([byte]$sizes[$i]); $w.Write([byte]$sizes[$i])
            $w.Write([byte]0); $w.Write([byte]0)
            $w.Write([uint16]1); $w.Write([uint16]32)
            $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
            $offset += $images[$i].Length
        }
        foreach ($img in $images) { $w.Write($img) }
    }
    finally {
        $w.Dispose()
        $fs.Dispose()
    }
}

Write-Ico (Join-Path $outDir 'idle.ico') $colors.idle ''
Write-Ico (Join-Path $outDir 'tracking-1.ico') $colors.tracking ''
foreach ($n in 2..9) { Write-Ico (Join-Path $outDir "tracking-$n.ico") $colors.parallel "$n" }
Write-Ico (Join-Path $outDir 'tracking-9plus.ico') $colors.parallel '9+'
Write-Ico (Join-Path $outDir 'attention-0.ico') $colors.attention ''
Write-Ico (Join-Path $outDir 'attention-1.ico') $colors.attention ''
foreach ($n in 2..9) { Write-Ico (Join-Path $outDir "attention-$n.ico") $colors.attention "$n" }
Write-Ico (Join-Path $outDir 'attention-9plus.ico') $colors.attention '9+'

$count = (Get-ChildItem -Path $outDir -Filter '*.ico').Count
Write-Output "Wrote $count icon files to $outDir"
