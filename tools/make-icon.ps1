# Draws the app icon (two session cards with a swap arrow) and writes app.ico
# with PNG frames at several sizes. Run once; app.ico is checked in.
Add-Type -AssemblyName System.Drawing
$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $u = $s / 32.0
    function Card($x, $y, $w, $h, $color) {
        $r = 3 * $u
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = 2 * $r
        $p.AddArc($x, $y, $d, $d, 180, 90)
        $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
        $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
        $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
        $p.CloseFigure()
        $g.FillPath((New-Object System.Drawing.SolidBrush $color), $p)
    }
    Card (2 * $u) (4 * $u) (13 * $u) (24 * $u) ([System.Drawing.Color]::FromArgb(59, 108, 246))
    Card (17 * $u) (4 * $u) (13 * $u) (24 * $u) ([System.Drawing.Color]::FromArgb(224, 130, 61))
    if ($s -ge 24) {
        $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 255, 255, 255))
        foreach ($cx in @(8.5, 23.5)) {
            foreach ($ly in @(9, 13.5)) {
                $g.FillRectangle($white, ($cx - 4) * $u, $ly * $u, 8 * $u, 1.6 * $u)
            }
        }
    }
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), (2.2 * $u)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    # arrow right (upper) and left (lower) across the gap
    $g.DrawLine($pen, 10 * $u, 19 * $u, 22 * $u, 19 * $u)
    $g.DrawLine($pen, 19 * $u, 16.5 * $u, 22 * $u, 19 * $u)
    $g.DrawLine($pen, 22 * $u, 24 * $u, 10 * $u, 24 * $u)
    $g.DrawLine($pen, 13 * $u, 26.5 * $u, 10 * $u, 24 * $u)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += , @($s, $ms.ToArray())
    $bmp.Dispose()
}
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $s = $f[0]; $data = $f[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$data.Length); $w.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f[1]) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot '..\app.ico'), $out.ToArray())
Write-Output "app.ico written ($($out.Length) bytes)"
