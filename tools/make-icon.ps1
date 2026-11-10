# Draws Scr's icon with WPF and writes src\Assets\scr.ico (PNG-compressed sizes 16-256).
# Run from anywhere:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName PresentationCore, WindowsBase, PresentationFramework

function C([string]$hex) { [System.Windows.Media.ColorConverter]::ConvertFromString($hex) }

function Draw-Icon([int]$s) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $full = New-Object System.Windows.Rect(0, 0, $s, $s)

    # Backdrop: a soft violet-to-pink gradient square with Windows 11 style rounding.
    $bg = New-Object System.Windows.Media.LinearGradientBrush
    $bg.StartPoint = New-Object System.Windows.Point(0, 0)
    $bg.EndPoint = New-Object System.Windows.Point(1, 1)
    $bg.GradientStops.Add((New-Object System.Windows.Media.GradientStop((C '#6E73F2'), 0)))
    $bg.GradientStops.Add((New-Object System.Windows.Media.GradientStop((C '#A77BF3'), 0.55)))
    $bg.GradientStops.Add((New-Object System.Windows.Media.GradientStop((C '#F08BC0'), 1)))
    $r = $s * 0.23
    $dc.DrawRoundedRectangle($bg, $null, $full, $r, $r)

    # The framed "screenshot": a white card with a little title bar line.
    $m = [Math]::Round($s * 0.24)
    $card = New-Object System.Windows.Rect($m, ($m + $s * 0.03), ($s - 2 * $m), ($s - 2 * $m - $s * 0.06))
    if ($s -ge 32) {
        $sh = New-Object System.Windows.Media.SolidColorBrush((C '#33240A50'))
        $shadow = New-Object System.Windows.Rect($card.X, ($card.Y + $s * 0.035), $card.Width, $card.Height)
        $dc.DrawRoundedRectangle($sh, $null, $shadow, $s * 0.07, $s * 0.07)
    }
    $dc.DrawRoundedRectangle([System.Windows.Media.Brushes]::White, $null, $card, $s * 0.07, $s * 0.07)
    if ($s -ge 24) {
        $bar = New-Object System.Windows.Media.SolidColorBrush((C '#C9C4F6'))
        $lineH = [Math]::Max(1.5, $s * 0.06)
        $bx = $card.X + $s * 0.08
        $dc.DrawRoundedRectangle($bar, $null, (New-Object System.Windows.Rect($bx, ($card.Y + $s * 0.09), ($card.Width * 0.45), $lineH)), $lineH / 2, $lineH / 2)
        $bar2 = New-Object System.Windows.Media.SolidColorBrush((C '#F3C6DE'))
        $dc.DrawRoundedRectangle($bar2, $null, (New-Object System.Windows.Rect($bx, ($card.Y + $s * 0.09 + $lineH * 2), ($card.Width * 0.7), ($card.Height * 0.38))), $s * 0.03, $s * 0.03)
    }

    # Viewfinder corners in white on the bigger sizes.
    if ($s -ge 32) {
        $pen = New-Object System.Windows.Media.Pen([System.Windows.Media.Brushes]::White, ($s * 0.045))
        $pen.StartLineCap = 'Round'; $pen.EndLineCap = 'Round'; $pen.LineJoin = 'Round'
        $pen.Brush = New-Object System.Windows.Media.SolidColorBrush((C '#E6FFFFFF'))
        $a = $s * 0.11; $l = $s * 0.13; $b = $s - $a
        $geo = [System.Windows.Media.Geometry]::Parse("M$a,$($a+$l) L$a,$a L$($a+$l),$a M$($b-$l),$a L$b,$a L$b,$($a+$l) M$b,$($b-$l) L$b,$b L$($b-$l),$b M$($a+$l),$b L$a,$b L$a,$($b-$l)")
        $dc.DrawGeometry($null, $pen, $geo)
    }
    $dc.Close()

    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($s, $s, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$pngs = @()
foreach ($s in $sizes) { $pngs += , (Draw-Icon $s) }

$out = Join-Path $PSScriptRoot '..\src\Assets\scr.ico'
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot '..\src\Assets\scr-256.png'), $pngs[-1])
Write-Output "Wrote $out"
