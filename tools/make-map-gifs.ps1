# Records the 표시 map's hover GIFs: each overlay window alone in each of its display styles, from the demo (--render-to, no
# screen capture), over the sample screen, cropped close round the window. Styles of one window are recorded from the same
# script times (--render-sim), so the card plays them in sync (user, 2026-11-10). Writes Assets/map/<Part>-<n>.gif and
# map.json (part → [label, file]), which MapPreview reads. Run after a window's look changes, then rebuild. Needs ffmpeg.
# Boss and Shadow (보스 체력, 대난투 체력: labels drawn over the game's own bar) are the labels over real footage instead: the
# real readers step them over the 세르카 clip (Tests/MapFootageSweep.cs) and ffmpeg burns them in as subtitles in the labels'
# font (OutlinedText: Malgun Gothic Bold, black outline), so the card shows the numbers changing (user, 2026-11-10).
#   tools/make-map-gifs.ps1 -Exe App\bin\Debug\net10.0-windows10.0.22621.0\GaugeOverlay.exe [-Only Meter,Amsu]
param([Parameter(Mandatory)][string]$Exe, [string[]]$Only = @(), [int]$MaxWidth = 400, [int]$Pad = 16,
      [string]$Video = (Join-Path (Split-Path $PSScriptRoot -Parent) 'reference\video-test\clips\boss-raid\serka-shadow-0850-0940.mp4'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'App\Assets\map'
$backdrop = Join-Path $root 'App\Assets\sample-screen.jpg'
$work = Join-Path $env:TEMP 'GaugeOverlay-map-gifs'
$manifestFile = Join-Path $out 'map.json'
New-Item -ItemType Directory -Force $out | Out-Null

# Each part: its loop in seconds (one whole cycle of PreviewSim's script: 단심 36, 디버프 30, 암수 24; the boxes' 자동 order
# 19 steps x 0.8 s; the gauge its own 4 s loop, GaugePlay.Looped: 85 % in 0.5 s, then spent by the class, user 2026-11-10),
# whether it runs on that script (Party needs the demo's peers), and its styles with the flags that pick them. Labels are the 설정 pages' names, the gauge's its
# 클래스 · 직업각인 (user, 2026-11-10); '' for a window with one style.
$parts = [ordered]@{
    Skills   = @{ Loop = 15.2; Sim = $true; Styles = @(
                  @('회전', @('--cooldown=Sweep')), @('채움', @('--cooldown=Fill')), @('채움 + 빗금', @('--cooldown=FillHatched'))) }
    Gauge    = @{ Loop = 4; Sim = $true; Styles = @(
                  @('워로드 · 전투태세', @('--as=워로드:전투태세:워리어', '--gauge-loop=on')), @('발키리 · 해방자', @('--as=발키리:해방자:워리어', '--gauge-loop=on')),
                  @('바드 · 절실한구원', @('--as=바드:절실한구원:마법사', '--gauge-loop=on'))) }
    Buffs    = @{ Loop = 15.2; Sim = $true; Styles = @(@('가로', @()), @('세로', @('--side-buffs'))) }
    Debuffs  = @{ Loop = 30; Sim = $true; Styles = @(, @('', @('--debuffs'))) }
    Judgment = @{ Loop = 36; Sim = $true; Styles = @(@('막대', @('--judgment')), @('아이콘', @('--judgment', '--judgment-icon'))) }
    Amsu     = @{ Loop = 24; Sim = $true; Styles = @(
                  @('막대', @('--amsu', '--amsu-look=막대')), @('원', @('--amsu', '--amsu-look=원')),
                  @('숫자', @('--amsu', '--amsu-look=숫자')), @('세로 막대', @('--amsu', '--amsu-look=세로막대'))) }
    Meter    = @{ Loop = 10; Sim = $true; Styles = @(
                  @('행', @('--meter', '--meter-solo', '--meter-look=행')), @('세로 막대', @('--meter', '--meter-solo', '--meter-look=세로막대')),
                  @('선 그래프', @('--meter', '--meter-solo', '--meter-look=선그래프')), @('한 줄 막대', @('--meter', '--meter-solo', '--meter-look=한줄막대'))) }
    BarCount = @{ Loop = 4; Sim = $true; Styles = @(, @('', @('--raid'))) }
    Party    = @{ Loop = 4; Sim = $false; Styles = @(, @('', @())) }
}

$manifest = [ordered]@{}
if (Test-Path $manifestFile) { (Get-Content $manifestFile -Raw -Encoding UTF8 | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $manifest[$_.Name] = $_.Value } }
foreach ($part in $parts.Keys) {
    if ($Only.Count -gt 0 -and $part -notin $Only) { continue }
    $p = $parts[$part]
    $frames = [int]($p.Loop * 10)
    $tiles = @()
    Get-ChildItem $out -Filter "$part*.gif" | Remove-Item   # the old single GIF and any style dropped since
    for ($i = 0; $i -lt $p.Styles.Count; $i++) {
        $label, $flags = $p.Styles[$i]
        $dir = Join-Path $work "$part-$i"
        if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
        $demo = @('--demo', "--only-part=$part", "--render-to=$dir", "--render-frames=$frames", '--render-every=100') + $flags
        if ($p.Sim) { $demo += '--render-sim=on' }
        Start-Process (Resolve-Path $Exe) -ArgumentList $demo -Wait
        $first = [System.Drawing.Image]::FromFile((Join-Path $dir 'f0000.png'))
        $sw, $sh = $first.Width, $first.Height
        $first.Dispose()
        # The window's area over all frames, padded a little: the tile shows the window with a strip of game round it.
        $seen = ffmpeg -hide_banner -framerate 10 -i (Join-Path $dir 'f%04d.png') -vf 'alphaextract,bbox=min_val=32' -f null - 2>&1 | Select-String 'x1:(\d+) x2:(\d+) y1:(\d+) y2:(\d+)'
        if (-not $seen) { throw "$part $label : nothing drawn" }
        $box = $seen | ForEach-Object { , ($_.Matches[0].Groups[1..4].Value | ForEach-Object { [int]$_ }) }
        $x1 = [math]::Max(0, ($box | ForEach-Object { $_[0] } | Measure-Object -Minimum).Minimum - $Pad)
        $x2 = [math]::Min($sw, ($box | ForEach-Object { $_[1] } | Measure-Object -Maximum).Maximum + $Pad)
        $y1 = [math]::Max(0, ($box | ForEach-Object { $_[2] } | Measure-Object -Minimum).Minimum - $Pad)
        $y2 = [math]::Min($sh, ($box | ForEach-Object { $_[3] } | Measure-Object -Maximum).Maximum + $Pad)
        $vw = [int](($x2 - $x1) / 2) * 2; $vh = [int](($y2 - $y1) / 2) * 2
        $file = "$part-$i.gif"
        $graph = "[1:v]scale=${sw}:${sh}[b];[b][0:v]overlay=shortest=1,crop=${vw}:${vh}:${x1}:${y1},scale='min($MaxWidth,iw)':-2:flags=lanczos,split[a][c];[a]palettegen=stats_mode=diff[p];[c][p]paletteuse=dither=none"
        ffmpeg -y -v error -framerate 10 -i (Join-Path $dir 'f%04d.png') -loop 1 -framerate 10 -i $backdrop -filter_complex $graph -loop 0 (Join-Path $out $file)
        if ($LASTEXITCODE -ne 0) { throw "$part $label : ffmpeg failed" }
        $tiles += [ordered]@{ Label = $label; File = $file }
        "{0} {1}: {2:n0} KB" -f $part, $label, ((Get-Item (Join-Path $out $file)).Length / 1KB)
    }
    $manifest[$part] = $tiles
}
# Each label over footage: the clip's seconds, the crop (1080p pixels: the boss's name and its label; 대난투's number, fill bar
# and dealt label), the GIF's width, and the labels as App draws them: sweep column, place (#rect), ink (ASS &HBBGGRR), centred,
# text share (OverlayTextWindow: InBar 0.85, the dealt 0.6, the boss 0.5).
$footage = [ordered]@{
    Boss   = @{ Start = 9.5; Len = 4; Crop = @(800, 24, 560, 46); Width = 440; Labels = @(, @(1, 'boss', 'FFFFFF', $false, 0.5)) }
    Shadow = @{ Start = 36; Len = 4; Crop = @(620, 50, 820, 80); Width = 560
                Labels = @(@(2, 'bar', 'FFE678', $true, 0.85), @(3, 'dealt', 'FFFFFF', $false, 0.6)) }
}
$emToAss = 1.33   # libass sizes Malgun Gothic by its ascent + descent (2724 of 2048 units), WPF by its em
function AssTime([double]$s) { '{0}:{1:00}:{2:00.00}' -f [int][math]::Floor($s / 3600), [int][math]::Floor($s / 60 % 60), ($s % 60) }
$todo = @($footage.Keys | Where-Object { $Only.Count -eq 0 -or $_ -in $Only })
if ($todo.Count -gt 0) {
    if (-not (Test-Path $Video)) { throw "no clip at $Video (-Video)" }
    New-Item -ItemType Directory -Force $work | Out-Null
    $tsv = Join-Path $work 'labels.tsv'
    $env:FFMPEG = (Get-Command ffmpeg).Source; $env:GAUGE_MAP_VIDEO = (Resolve-Path $Video).Path; $env:GAUGE_SWEEP_OUT = $tsv
    dotnet test (Join-Path $root 'Tests') -nologo -v q --filter FullyQualifiedName~MapFootageSweep | Out-Null
    $failed = $LASTEXITCODE -ne 0
    Remove-Item Env:GAUGE_MAP_VIDEO, Env:GAUGE_SWEEP_OUT
    if ($failed -or -not (Test-Path $tsv)) { throw 'MapFootageSweep failed' }
    $lines = Get-Content $tsv -Encoding UTF8
    $rects = @{}
    foreach ($l in $lines | Where-Object { $_ -like '#rect *' }) { $f = $l.Split(' '); $rects[$f[1]] = [int[]]$f[2..5] }
    $rows = @($lines | Where-Object { $_ -notlike '#*' } | ForEach-Object { , $_.Split("`t") })
    foreach ($part in $todo) {
        $p = $footage[$part]
        $ass = @('[Script Info]', 'ScriptType: v4.00+', 'PlayResX: 1920', 'PlayResY: 1080', 'ScaledBorderAndShadow: yes', '', '[V4+ Styles]',
            'Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding',
            'Style: L,Malgun Gothic,20,&H00FFFFFF,&H00FFFFFF,&H00000000,&H00000000,-1,0,0,0,100,100,0,0,1,1,0,4,0,0,0,1', '', '[Events]',
            'Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text')
        foreach ($row in $rows) {
            $t = [double]::Parse($row[0], [Globalization.CultureInfo]::InvariantCulture) - $p.Start
            if ($t -lt -0.05 -or $t -ge $p.Len) { continue }   # each row holds ±0.05 s round its frame: a frame never sits on an edge
            foreach ($label in $p.Labels) {
                $col, $name, $ink, $centred, $share = $label
                if ($row.Count -le $col -or $row[$col] -eq '') { continue }
                $x, $y, $w, $h = $rects[$name]
                $size = $h * $share; $pen = $size * 0.2   # OutlinedText: the pen is 0.2 of the size, half of it shows
                $at = if ($centred) { '\an5\pos({0},{1})' -f ($x + $w / 2), ($y + $h / 2) } else { '\an4\pos({0},{1})' -f ($x + $pen / 2), ($y + $h / 2) }
                $ass += 'Dialogue: 0,{0},{1},L,,0,0,0,,{{{2}\fs{3:0.#}\bord{4:0.##}\c&H{5}&}}{6}' -f (AssTime ([math]::Max(0.0, $t - 0.05))), (AssTime ($t + 0.05)), $at, ($size * $emToAss), ($pen / 2), $ink, $row[$col]
            }
        }
        $ass | Set-Content (Join-Path $work "$part.ass") -Encoding UTF8
        $cw, $ch, $cx, $cy = $p.Crop[2], $p.Crop[3], $p.Crop[0], $p.Crop[1]
        $file = "$part-0.gif"
        $graph = "setpts=PTS-STARTPTS,fps=10,subtitles=$part.ass,crop=${cw}:${ch}:${cx}:${cy},scale=$($p.Width):-2:flags=lanczos,split[a][c];[a]palettegen=stats_mode=diff[q];[c][q]paletteuse=dither=bayer:bayer_scale=3"
        Push-Location $work   # subtitles= takes a plain name: a Windows path's drive colon needs escaping in a filter
        try { ffmpeg -y -v error -ss $p.Start -t $p.Len -i (Resolve-Path $Video).Path -filter_complex $graph -loop 0 (Join-Path $out $file) }
        finally { Pop-Location }
        if ($LASTEXITCODE -ne 0) { throw "$part : ffmpeg failed" }
        $manifest[$part] = @([ordered]@{ Label = ''; File = $file })
        "{0}: {1:n0} KB" -f $part, ((Get-Item (Join-Path $out $file)).Length / 1KB)
    }
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content $manifestFile -Encoding UTF8
