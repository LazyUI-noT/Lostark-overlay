# Records the 표시 map's hover GIFs: each overlay window alone, from the demo (--render-to, no screen capture), over the sample
# screen. Run after a window's look changes, then rebuild. Needs ffmpeg on the PATH.
#   tools/make-map-gifs.ps1 -Exe App\bin\Debug\net10.0-windows10.0.22621.0\GaugeOverlay.exe [-Only Meter,Amsu]
param([Parameter(Mandatory)][string]$Exe, [string[]]$Only = @(), [int]$Width = 480, [int]$Frames = 40, [int]$MinView = 640)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'App\Assets\map'
$backdrop = Join-Path $root 'App\Assets\sample-screen.jpg'
$work = Join-Path $env:TEMP 'GaugeOverlay-map-gifs'
New-Item -ItemType Directory -Force $out | Out-Null

# Each part with the demo flags that bring its window up (OverlayPart names: the file names MapPreview loads).
$parts = [ordered]@{
    Skills = @(); Gauge = @(); Buffs = @(); Debuffs = @('--debuffs'); Judgment = @(); Amsu = @('--amsu')
    Meter = @('--meter'); BarCount = @('--raid'); Party = @()
}
foreach ($part in $parts.Keys) {
    if ($Only.Count -gt 0 -and $part -notin $Only) { continue }
    $dir = Join-Path $work $part
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    $demo = @('--demo', "--only-part=$part", "--render-to=$dir", "--render-frames=$Frames", '--render-every=100') + $parts[$part]
    Start-Process (Resolve-Path $Exe) -ArgumentList $demo -Wait
    $first = [System.Drawing.Image]::FromFile((Join-Path $dir 'f0000.png'))
    $sw, $sh = $first.Width, $first.Height
    $first.Dispose()
    # The window's area over all frames, then a 16:9 view round it: big enough to read it, with the game around it to show where it sits.
    $seen = ffmpeg -hide_banner -framerate 10 -i (Join-Path $dir 'f%04d.png') -vf 'alphaextract,bbox=min_val=32' -f null - 2>&1 | Select-String 'x1:(\d+) x2:(\d+) y1:(\d+) y2:(\d+)'
    if (-not $seen) { throw "$part : nothing drawn" }
    $box = $seen | ForEach-Object { , ($_.Matches[0].Groups[1..4].Value | ForEach-Object { [int]$_ }) }
    $x1 = ($box | ForEach-Object { $_[0] } | Measure-Object -Minimum).Minimum; $x2 = ($box | ForEach-Object { $_[1] } | Measure-Object -Maximum).Maximum
    $y1 = ($box | ForEach-Object { $_[2] } | Measure-Object -Minimum).Minimum; $y2 = ($box | ForEach-Object { $_[3] } | Measure-Object -Maximum).Maximum
    $vw = [math]::Min($sw, [math]::Max($MinView, [math]::Max(($x2 - $x1) * 1.5, ($y2 - $y1) * 1.5 * 16 / 9)))
    $vh = [int]($vw * 9 / 16 / 2) * 2; $vw = [int]($vw / 2) * 2
    $vx = [int][math]::Max(0, [math]::Min($sw - $vw, ($x1 + $x2 - $vw) / 2)); $vy = [int][math]::Max(0, [math]::Min($sh - $vh, ($y1 + $y2 - $vh) / 2))
    $graph = "[1:v]scale=${sw}:${sh}[b];[b][0:v]overlay=shortest=1,crop=${vw}:${vh}:${vx}:${vy},scale=${Width}:-2:flags=lanczos,split[a][c];[a]palettegen=stats_mode=diff[p];[c][p]paletteuse=dither=none"
    ffmpeg -y -v error -framerate 10 -i (Join-Path $dir 'f%04d.png') -loop 1 -framerate 10 -i $backdrop -filter_complex $graph -loop 0 (Join-Path $out "$part.gif")
    if ($LASTEXITCODE -ne 0) { throw "$part : ffmpeg failed" }
    "{0}: {1:n0} KB" -f $part, ((Get-Item (Join-Path $out "$part.gif")).Length / 1KB)
}
