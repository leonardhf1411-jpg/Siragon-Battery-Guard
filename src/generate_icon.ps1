Add-Type -AssemblyName System.Drawing

function New-BatteryBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [float]$size

    # Helper for rounded rectangle path
    function Get-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $r * 2.0
        if ($d -gt $w) { $d = $w }
        if ($d -gt $h) { $d = $h }
        $path.AddArc($x, $y, $d, $d, 180, 90)
        $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
        $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
        $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
        $path.CloseFigure()
        return $path
    }

    # 1. Dark squircle background badge
    $pad = $s * 0.04
    $bgW = $s - ($pad * 2)
    $bgPath = Get-RoundRect $pad $pad $bgW $bgW ($s * 0.22)
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)),
        (New-Object System.Drawing.PointF($s, $s)),
        [System.Drawing.Color]::FromArgb(255, 18, 25, 38),
        [System.Drawing.Color]::FromArgb(255, 10, 14, 22)
    )
    $g.FillPath($bgBrush, $bgPath)

    $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(140, 56, 189, 248), [Math]::Max(1.0, $s * 0.025))
    $g.DrawPath($borderPen, $bgPath)

    # 2. Battery positive terminal cap (top)
    $capW = $s * 0.22
    $capH = $s * 0.075
    $capX = ($s - $capW) / 2.0
    $capY = $s * 0.14
    $capPath = Get-RoundRect $capX $capY $capW ($capH + ($s * 0.03)) ($s * 0.03)
    $capBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 148, 163, 184))
    $g.FillPath($capBrush, $capPath)

    # 3. Battery outer body
    $bodyW = $s * 0.48
    $bodyH = $s * 0.62
    $bodyX = ($s - $bodyW) / 2.0
    $bodyY = $s * 0.20
    $bodyR = $s * 0.07

    $bodyPath = Get-RoundRect $bodyX $bodyY $bodyW $bodyH $bodyR
    $bodyFill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 15, 23, 34))
    $g.FillPath($bodyFill, $bodyPath)

    $shellPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 226, 232, 240), [Math]::Max(1.5, $s * 0.045))
    $g.DrawPath($shellPen, $bodyPath)

    # 4. Inner battery charge level (80% emerald/cyan gradient)
    $inPad = $s * 0.055
    $inX = $bodyX + $inPad
    $inW = $bodyW - ($inPad * 2.0)
    $maxInH = $bodyH - ($inPad * 2.0)
    $fillH = $maxInH * 0.80
    $inY = ($bodyY + $bodyH - $inPad) - $fillH

    $fillPath = Get-RoundRect $inX $inY $inW $fillH ($s * 0.035)
    $chargeBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF($inX, $inY)),
        (New-Object System.Drawing.PointF($inX, ($inY + $fillH))),
        [System.Drawing.Color]::FromArgb(255, 56, 189, 248),
        [System.Drawing.Color]::FromArgb(255, 34, 197, 94)
    )
    $g.FillPath($chargeBrush, $fillPath)

    # 5. 80% limit dashed/glowing marker line near top of fill
    $limitPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(230, 250, 204, 21), [Math]::Max(1.0, $s * 0.025))
    $g.DrawLine($limitPen, $inX, $inY, ($inX + $inW), $inY)

    # 6. Lightning bolt in the center
    $cx = $s * 0.50
    $cy = $bodyY + ($bodyH * 0.52)
    $bw = $bodyW * 0.42
    $bh = $bodyH * 0.48

    $boltPoints = @(
        (New-Object System.Drawing.PointF(($cx + $bw * 0.10), ($cy - $bh * 0.50))),
        (New-Object System.Drawing.PointF(($cx - $bw * 0.45), ($cy + $bh * 0.06))),
        (New-Object System.Drawing.PointF(($cx - $bw * 0.02), ($cy + $bh * 0.06))),
        (New-Object System.Drawing.PointF(($cx - $bw * 0.14), ($cy + $bh * 0.50))),
        (New-Object System.Drawing.PointF(($cx + $bw * 0.45), ($cy - $bh * 0.06))),
        (New-Object System.Drawing.PointF(($cx + $bw * 0.02), ($cy - $bh * 0.06)))
    )

    $boltBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 254, 240, 138))
    $boltPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(220, 15, 23, 42), [Math]::Max(1.0, $s * 0.02))
    $g.FillPolygon($boltBrush, $boltPoints)
    if ($size -ge 32) {
        $g.DrawPolygon($boltPen, $boltPoints)
    }

    $g.Dispose()
    return $bmp
}

$sizes = @(256, 64, 48, 32, 16)
$pngStreams = New-Object System.Collections.Generic.List[byte[]]

foreach ($sz in $sizes) {
    $bmp = New-BatteryBitmap $sz
    if ($sz -eq 256) {
        $previewPath = Join-Path $PSScriptRoot "battery_preview.png"
        $bmp.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngStreams.Add($ms.ToArray())
    $ms.Dispose()
    $bmp.Dispose()
}

$icoPath = Join-Path $PSScriptRoot "battery.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR header
$bw.Write([uint16]0)               # Reserved
$bw.Write([uint16]1)               # Type: 1 = ICO
$bw.Write([uint16]$sizes.Count)    # Count

$offset = 6 + (16 * $sizes.Count)

for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $data = $pngStreams[$i]
    $dimByte = if ($sz -ge 256) { [byte]0 } else { [byte]$sz }

    $bw.Write($dimByte)            # Width
    $bw.Write($dimByte)            # Height
    $bw.Write([byte]0)             # ColorCount
    $bw.Write([byte]0)             # Reserved
    $bw.Write([uint16]1)           # Planes
    $bw.Write([uint16]32)          # BitCount
    $bw.Write([uint32]$data.Length)# BytesInRes
    $bw.Write([uint32]$offset)     # ImageOffset

    $offset += $data.Length
}

for ($i = 0; $i -lt $sizes.Count; $i++) {
    $bw.Write($pngStreams[$i])
}

$bw.Flush()
$bw.Dispose()
$fs.Dispose()

Write-Host "Icono generado exitosamente en: $icoPath" -ForegroundColor Green
