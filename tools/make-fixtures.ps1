# Cuts small raw BGRA slot crops from reference/ into Tests/Fixtures, so tests need no image decoding
# and the large videos stay out of git. Requires ffmpeg on PATH. Re-run after changing a sequence below.
#
# Output per fixture: <name>.bgra (frames back to back, W*H*4 bytes each) + <name>.json (size, per-frame pts).
# Crop rects come from the same formula as Core/Vision/Geometry.cs (reference 110%, bottom-centre origin).

# -VideoDir: folder holding gameplay.mp4 / 333.mp4 (gitignored, so a worktree may point at another checkout).
param([string]$VideoDir)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$ref = Join-Path $root 'reference'
if (-not $VideoDir) { $VideoDir = $ref }
$out = Join-Path $root 'Tests/Fixtures'
New-Item -ItemType Directory -Force $out | Out-Null

# Keep in sync with Geometry.RefSlots (GeometryTests checks this table against Geometry).
$slots = [ordered]@{
  '각성기'=@(514,991,56,55); '초각스'=@(592,992,54,54)
  '일반1'=@(654,969,51,49); '일반2'=@(706,969,51,49); '일반3'=@(758,969,51,49); '일반4'=@(810,969,50,49)
  '일반5'=@(681,1021,50,49); '일반6'=@(732,1021,50,49); '일반7'=@(783,1021,50,49); '일반8'=@(834,1021,49,49)
  '아덴1'=@(865,964,46,55); '아덴2'=@(1009,964,46,55)
}

function Rect($slot, $hud) { RefRect $slots[$slot] $hud }

# $bottom, $vpScale: a viewport other than 1920×1080 (its bottom row and height / 1080).
function RefRect($r, $hud, $bottom = 1080, $vpScale = 1) {
  $k = $hud / 110.0 * $vpScale
  @([int][math]::Round(960 + ($r[0] - 960) * $k), [int][math]::Round($bottom + ($r[1] - 1080) * $k),
    [int][math]::Round($r[2] * $k), [int][math]::Round($r[3] * $k))
}

function Cut($name, $src, $slot, $hud, $start = $null, $duration = $null) { CutRect $name $src $slot $hud (Rect $slot $hud) $start $duration }

# format=bgra before crop: cropping yuv420 video directly rounds odd sizes and offsets down to even.
# -t before -i, so showinfo sees exactly the frames that are written.
function CutRect($name, $src, $slot, $hud, $r, $start = $null, $duration = $null) {
  $vf = "format=bgra,crop=$($r[2]):$($r[3]):$($r[0]):$($r[1]),showinfo"
  $ffArgs = @('-v', 'info', '-y')
  if ($null -ne $start) { $ffArgs += @('-ss', "$start") }
  if ($null -ne $duration) { $ffArgs += @('-t', "$duration") }
  $ffArgs += @('-i', $src)
  $ffArgs += @('-vf', $vf, '-f', 'rawvideo', '-pix_fmt', 'bgra', (Join-Path $out "$name.bgra"))
  $log = & ffmpeg @ffArgs 2>&1 | Out-String
  $pts = [regex]::Matches($log, 'pts_time:([0-9.]+)') | ForEach-Object { [double]$_.Groups[1].Value + [double]($start ?? 0) }
  [ordered]@{ source = (Split-Path $src -Leaf); slot = $slot; hud = $hud; rect = $r; width = $r[2]; height = $r[3]; pts = @($pts) } |
    ConvertTo-Json -Depth 3 | Set-Content -Encoding utf8NoBOM (Join-Path $out "$name.json")
  Write-Host "$name  $($pts.Count) frame(s)  rect $($r -join ',')"
}

$game = Join-Path $VideoDir 'gameplay.mp4'
$v333 = Join-Path $VideoDir '333.mp4'
# gameplay.mp4 was recorded at HUD 100%, 333.mp4 at HUD 110% (its 각성기 frame is 75 px wide).
Cut 'gameplay_D_end'   $game '일반7' 100 7.9 1.9     # 2s->1s at frame 39, ready at frame 99
Cut 'gameplay_S_end'   $game '일반6' 100 10.9 1.3    # 2s->1s at frame 7, ready at frame 67
Cut 'gameplay_D_start' $game '일반7' 100 3.3 0.5     # 7s->6s at frame 15
Cut 'gameplay_S_cutscene' $game '일반6' 100 16.0 4.5 # HUD hidden during the 각성기 cutscene
Cut '333_E' $v333 '일반3' 110 15.6 0.8                # cast flash at 20, 25s at 21
Cut '333_C' $v333 '아덴2' 110 15.6 0.8                # 17s, reset (digits gone) at 21

# Still screenshots at HUD 110%: every slot.
foreach ($shot in @('No cd', 'cd', 'nomana', 'silenced')) {
  foreach ($slot in $slots.Keys) { Cut "$($shot -replace ' ', '')_$slot" (Join-Path $ref "$shot.png") $slot 110 }
}

# Digit glyph sources at HUD 110 (333.mp4), one frame each; the text shown is in the name.
foreach ($g in @(@('20s_W', '일반2', 18.5), @('22s_E', '일반3', 18.5), @('56s_T', '초각스', 18.5), @('23s_W', '일반2', 9.0),
                 @('18s_C', '아덴2', 9.0), @('17s_W', '일반2', 21.0), @('54s_T', '초각스', 21.0), @('19s_W', '일반2', 19.0),
                 @('18s_W', '일반2', 20.0), @('21s_E', '일반3', 20.0))) {
  Cut "glyph_$($g[0])" $v333 $g[1] 110 $g[2] 0.016
}

# Bottom bands for FitSolver (screen rects; the 각성기 frame lies inside at every HUD size).
foreach ($b in @(@('80', '80percent.png', 80), @('90', '90percent.png', 90), @('105', '105percent.png', 105),
                 @('110', '110percent.png', 110), @('Nocd', 'No cd.png', 110))) {
  CutRect "band_$($b[0])" (Join-Path $ref $b[1]) 'band' $b[2] @(400, 900, 360, 180)
}
CutRect 'band_219' (Join-Path $ref 'forced_219.png') 'band' 110 @(400, 810, 360, 135)   # viewport 0,135,1920,810
CutRect 'band_gameplay' $game 'band' 100 @(400, 900, 360, 180) 9.0 0.016
CutRect 'band_333' $v333 'band' 110 @(400, 900, 360, 180) 9.0 0.016

# Class emblem (Geometry.RefEmblem) with an 8 px margin, HUD 110.
foreach ($shot in @('No cd', 'cd', 'main focus')) {
  CutRect "emblem_$($shot -replace ' ', '')" (Join-Path $ref "$shot.png") 'emblem' 110 @(904, 922, 112, 158)
}

# 각성기 변화 label: the whole reference crop (HUD 110), and the area above 각성기 in gameplay.mp4 (HUD 100)
# with the label absent (5.0 s), grey while 각성기 is on cooldown (14.25 s) and red (15.5 s).
CutRect 'label_ref' (Join-Path $ref '각성기변화.PNG') 'label' 110 @(0, 0, 121, 161)
foreach ($t in @(5.0, 14.25, 15.5)) { CutRect "label_gameplay_$t" $game 'label' 100 (RefRect @(495, 940, 95, 45) 100) $t 0.016 }
# The grey banner with another key binding (gauge full, 각성기 on cooldown), and dark scenes without a banner that come
# closest to its dark body (gameplay 5.5 s / 23.8 s, live test 5.2 s).
CutRect 'label_ref2' (Join-Path $ref '각성기변화2.png') 'label' 110 @(0, 0, 94, 123)
foreach ($t in @(5.5, 23.8)) { CutRect "label_gameplay_$t" $game 'label' 100 (RefRect @(495, 940, 95, 45) 100) $t 0.016 }
CutRect 'label_lt_5.2' (Join-Path $VideoDir 'live test.mp4') 'label' 110 (RefRect @(495, 940, 95, 45) 110) 5.2 0.016
# 각성기 변화_sample.mp4 (HUD 110): the banner stays up the whole clip over changing backgrounds — grey on the dark void
# (1.0 s), red on the bright floor (5.5 s), grey on the bright floor (9.5, 10.5 s).
foreach ($t in @(1.0, 5.5, 9.5, 10.5)) { CutRect "label_sample_$t" (Join-Path $ref '각성기 변화_sample.mp4') 'label' 110 (RefRect @(495, 940, 95, 45) 110) $t 0.016 }

# Letterbox.Probe columns (5% in from the left, full height): forced 21:9 bars, and none.
CutRect 'probe_219' (Join-Path $ref 'forced_219.png') 'probe' 110 @(96, 0, 2, 1080)
CutRect 'probe_Nocd' (Join-Path $ref 'No cd.png') 'probe' 110 @(96, 0, 2, 1080)

# The 각성기 frame (Geometry.RefAwakeningFrame) in more HUD 110 shots; the frame template is their median.
foreach ($shot in @('cd', 'nomana', 'silenced')) { CutRect "frame_$shot" (Join-Path $ref "$shot.png") 'frame' 110 @(505, 982, 75, 73) }

# live_test2.mp4 (HUD 110, a different character from the screenshots): the bar clear at 2.0 s, and at 0.3 s with
# the Windows taskbar over the bottom row. The "other_" crops these are compared with are the ready icons 스킬바
# 맞추기 saved on the screenshots' character (%APPDATA%\GaugeOverlay\calibration, 2026-09-22), converted once from
# that folder rather than cut here.
$lt2 = Join-Path $VideoDir 'live_test2.mp4'
foreach ($slot in @('각성기', '일반1', '일반2', '일반3', '일반4', '일반5', '일반6', '일반7', '일반8')) { Cut "lt2_clear_$slot" $lt2 $slot 110 2.0 0.016 }
foreach ($slot in @('일반5', '일반6', '일반7', '일반8')) { Cut "lt2_taskbar_$slot" $lt2 $slot 110 0.3 0.016 }

# Stack count "2" (Q with both uses, no timer): the count font's own '2', which the timer's '2' doesn't match.
Cut 'lt2_count2_일반1' $lt2 '일반1' 110 10.0 0.016
Cut 'lt_count2_일반1' (Join-Path $VideoDir 'live test.mp4') '일반1' 110 22.0 0.016

# Single digits read with a phantom tens '1' (live test.mp4): the edge of the dark cooldown sweep left of the digit
# matches the '1' template. 초각스 "6s" → 16, "3s" → 13; 일반1 "1s" → 11.
$lt = Join-Path $VideoDir 'live test.mp4'
Cut 'lt_6_초각스' $lt '초각스' 110 11.62 0.016
Cut 'lt_3_초각스' $lt '초각스' 110 14.62 0.016
Cut 'lt_1_일반1' $lt '일반1' 110 14.44 0.016
# A stack skill's first cast, every frame for 1.2 s: "1s" without its count for ~150 ms, the count, then the recharge timer
# (E 4.08 s, Q 3.05 s). The sweep must not jump in it (CooldownTracker: a known stack skill's "1s" is the lockout).
Cut 'lt2_lockout_일반3' $lt2 '일반3' 110 4.0 1.2
Cut 'lt_lockout_일반1' $lt '일반1' 110 3.0 1.2
# gameplay.mp4 (HUD 100) just after S is cast: the white sweep line at 12 o'clock over "8s" matched a tens '1'
# overlapping the 8, so it read 18 for three frames.
Cut 'gameplay_8_일반6' (Join-Path $VideoDir 'gameplay.mp4') '일반6' 100 23.24 0.016

# Gold frame (a buff lights some skills' frames gold, PLAN §3 "Gold frame"). 333.mp4: at 4.0 s Q W E R F are lit and the
# blue S D aren't, at 3.0 s nothing is. live test.mp4 1.0 s: E R lit, Q W not. gameplay.mp4 (HUD 100): lit at 26.5 s, not at 20 s.
foreach ($slot in @('일반1', '일반2', '일반3', '일반4', '일반6', '일반7', '일반8')) {
  Cut "gold_333_4_$slot" $v333 $slot 110 4.0 0.016
  Cut "gold_333_3_$slot" $v333 $slot 110 3.0 0.016
}
foreach ($slot in @('일반1', '일반2', '일반3', '일반4')) {
  Cut "gold_lt_1_$slot" $lt $slot 110 1.0 0.016
  Cut "gold_gameplay_26.5_$slot" $game $slot 100 26.5 0.016
  Cut "gold_gameplay_20_$slot" $game $slot 100 20.0 0.016
}

# 4K references (app-wide, PLAN §1a). 4k_screenshot_bard.jpg: 3840×2160, no bars. 4k_21_9_screenshot_bard.png: forced
# 21:9 at 4K, viewport 0,270,3840,1620. 4k_down.mp4: a 4K forced
# 21:9 recording scaled to 1920×1080, viewport 0,135,1920,810 (lossy too).
$k4 = Join-Path $ref '4k_screenshot_bard.jpg'
$k219 = Join-Path $ref '4k_21_9_screenshot_bard.png'
$down = Join-Path $VideoDir '4k_down.mp4'
CutRect 'band_4k' $k4 'band' 100 @(950, 1850, 560, 310)
CutRect 'band_4k219' $k219 'band' 100 @(1150, 1650, 500, 240)
# The class emblem (Geometry.RefEmblem) at HUD 100: 4K scale 1.82 about (1920, 2160), 21:9 scale 1.36 about (1920, 1890).
CutRect 'emblem_4k_바드' $k4 'emblem' 100 @(1833, 1945, 175, 196)
CutRect 'emblem_4k219_바드' $k219 'emblem' 100 @(1855, 1729, 131, 147)

# In town the game greys the skill bar out (greyed out.png, 워로드 고독한기사, HUD 110): the bar for the fit, the emblem, and
# every slot, for telling a greyed-out bar (SubclassMatcher.Greyed) and the 초각스's 직업각인 by its shapes.
$greyed = Join-Path $ref 'greyed out.png'
CutRect 'band_greyed' $greyed 'band' 110 @(400, 900, 360, 180)
CutRect 'emblem_greyed' $greyed 'emblem' 110 @(912, 962, 96, 108)
foreach ($s in $slots.Keys) { Cut "greyed_$s" $greyed $s 110 }

CutRect 'band_down' $down 'band' 100 @(400, 810, 360, 135) 30.0 0.016   # V on cooldown; 0–13 s it glows red
CutRect 'probe_4k219' $k219 'probe' 100 @(192, 0, 2, 2160)
# Slots in 4k_down.mp4 at 30 s (HUD 100 in the 810-high viewport, scale 0.68, the smallest tested): digits at that scale.
foreach ($d in @(@('4s', '일반1'), @('10s', '일반3'), @('22s', '초각스'), @('53s', '각성기'))) {
  CutRect "down_$($d[0])_$($d[1])" $down $d[1] 100 (RefRect $slots[$d[1]] 100 945 0.75) 30.0 0.016
}

# Buff row (PLAN §4 버프): the five rightmost boxes and their timers. The row's bottom-right corner (844, 925 at HUD 110)
# follows the HUD; the icons scale with the in-game buff size × viewport height, not the HUD.
# buff_gameplay.mp4 (발키리 해방자): 1920×1080, HUD 110, buff 120%, whole clip at 5 fps. 4k_down.mp4 (바드): viewport
# 0,135,1920,810, HUD 100, buff 100%, whole clip at 5 fps. buff_wrong_detect.mp4 (발키리 해방자, set up as buff_gameplay): its own
# gold dome buff, a look-alike of 아덴피증; whole clip at 5 fps.
$bg = Join-Path $VideoDir 'buff_gameplay.mp4'
CutRect 'band_buff' $bg 'band' 110 @(400, 900, 360, 180) 18.0 0.016
foreach ($spec in @(@('buffrow_bg', $bg, @(664, 886, 184, 60)), @('buffrow_wrong', (Join-Path $VideoDir 'buff_wrong_detect.mp4'), @(664, 886, 184, 60)), @('buffrow_down', $down, @(760, 815, 124, 40)))) {
  $r = $spec[2]
  $log = & ffmpeg -v info -y -i $spec[1] -vf "fps=5,format=bgra,crop=$($r[2]):$($r[3]):$($r[0]):$($r[1]),showinfo" -f rawvideo -pix_fmt bgra (Join-Path $out "$($spec[0]).bgra") 2>&1 | Out-String
  $pts = [regex]::Matches($log, 'pts_time:([0-9.]+)') | ForEach-Object { [double]$_.Groups[1].Value }
  [ordered]@{ source = (Split-Path $spec[1] -Leaf); slot = 'buffs'; hud = 0; rect = $r; width = $r[2]; height = $r[3]; pts = @($pts) } |
    ConvertTo-Json -Depth 3 | Set-Content -Encoding utf8NoBOM (Join-Path $out "$($spec[0]).json")
  Write-Host "$($spec[0])  $($pts.Count) frame(s)"
}

# 1sdoublebuff.mp4 (도화가 party, set up as buff_gameplay): an 8 s 공증, then the 1 s 공증's AoE icon beside it; 10 fps.
$r = @(664, 886, 184, 60)
$log = & ffmpeg -v info -y -i (Join-Path $VideoDir '1sdoublebuff.mp4') -vf "fps=10,format=bgra,crop=$($r[2]):$($r[3]):$($r[0]):$($r[1]),showinfo" -f rawvideo -pix_fmt bgra (Join-Path $out 'buffrow_dbl.bgra') 2>&1 | Out-String
$pts = [regex]::Matches($log, 'pts_time:([0-9.]+)') | ForEach-Object { [double]$_.Groups[1].Value }
[ordered]@{ source = '1sdoublebuff.mp4'; slot = 'buffs'; hud = 0; rect = $r; width = $r[2]; height = $r[3]; pts = @($pts) } |
  ConvertTo-Json -Depth 3 | Set-Content -Encoding utf8NoBOM (Join-Path $out 'buffrow_dbl.json')
Write-Host "buffrow_dbl  $($pts.Count) frame(s)"
# reference/subclass/도화가/도화가support.mp4 and (2).mp4 (도화가 서폿, same setup): 8 s 공증 casts and the 1 s AoE, with
# last-second timers whose "0." the reader drops; 10 fps, a wider crop.
$r = @(600, 870, 260, 90)
foreach ($spec in @(@('buffrow_dohwa1', '도화가support.mp4'), @('buffrow_dohwa2', '도화가support (2).mp4'))) {
  $log = & ffmpeg -v info -y -i (Join-Path $ref "subclass/도화가/$($spec[1])") -vf "fps=10,format=bgra,crop=$($r[2]):$($r[3]):$($r[0]):$($r[1]),showinfo" -f rawvideo -pix_fmt bgra (Join-Path $out "$($spec[0]).bgra") 2>&1 | Out-String
  $pts = [regex]::Matches($log, 'pts_time:([0-9.]+)') | ForEach-Object { [double]$_.Groups[1].Value }
  [ordered]@{ source = $spec[1]; slot = 'buffs'; hud = 0; rect = $r; width = $r[2]; height = $r[3]; pts = @($pts) } |
    ConvertTo-Json -Depth 3 | Set-Content -Encoding utf8NoBOM (Join-Path $out "$($spec[0]).json")
  Write-Host "$($spec[0])  $($pts.Count) frame(s)"
}
# 공증bug.mp4 (same setup): an 8 s 공증 at "1초" when the 1 s AoE's icon joins it (2.8 s); the 8 s one drains to 0 and
# goes, the AoE one stays at "1초" while stood on, then drains to 0 (7.4 s); 10 fps, the 도화가 crop.
$log = & ffmpeg -v info -y -i (Join-Path $ref '공증bug.mp4') -vf "fps=10,format=bgra,crop=$($r[2]):$($r[3]):$($r[0]):$($r[1]),showinfo" -f rawvideo -pix_fmt bgra (Join-Path $out 'buffrow_gzbug.bgra') 2>&1 | Out-String
$pts = [regex]::Matches($log, 'pts_time:([0-9.]+)') | ForEach-Object { [double]$_.Groups[1].Value }
[ordered]@{ source = '공증bug.mp4'; slot = 'buffs'; hud = 0; rect = $r; width = $r[2]; height = $r[3]; pts = @($pts) } |
  ConvertTo-Json -Depth 3 | Set-Content -Encoding utf8NoBOM (Join-Path $out 'buffrow_gzbug.json')
Write-Host "buffrow_gzbug  $($pts.Count) frame(s)"

# buff_80_to_110.mp4 (발키리): 1920×1080, HUD 110, the in-game buff size stepped 80 → 90 → 100 → 110 through the
# settings window; whole clip at 2 fps. 1440p_hud100_120buff.png: a two-monitor shot, the game at 1080,162 2560×1440
# (HUD 100, buff 120%), one food buff ("3분") and no party buff.
$b80 = Join-Path $VideoDir 'buff_80_to_110.mp4'
$q = Join-Path $ref '1440p_hud100_120buff.png'
CutRect 'band_b80' $b80 'band' 110 @(400, 900, 360, 180) 2.0 0.016
CutRect 'band_1440' $q 'band' 100 @(1713, 1395, 373, 206)
# band_bard_charged: bardbug_103.mp4 (바드, HUD 100, charged 각성기 frame) at 1, 10 and 20 s, rect 400,880,360,200: cut by hand,
# since CutRect writes one span only.
CutRect 'buffrow_1440' $q 'buffs' 100 @(1990, 1360, 240, 85)
# HUD80_buff120.png / HUD90_buff120.png (1920×1080, buff 120%): the row packed tighter below HUD 100, the four party buffs in
# boxes 0–3 and other buffs further left.
CutRect 'buffrow_hud80' (Join-Path $ref 'HUD80_buff120.png') 'buffs' 80 @(630, 925, 250, 60)
CutRect 'buffrow_hud90' (Join-Path $ref 'HUD90_buff120.png') 'buffs' 90 @(610, 912, 260, 60)
# discord_stream.png: a screen grab of a Discord full-screen stream (바드, HUD ~90, forced 21:9: game area 1920×810 from row 135),
# blurred, its bars 1–3 rather than 0. The black-bar probe column and the bottom band of the game area.
$disc = Join-Path $ref 'discord_stream.png'
CutRect 'probe_discord' $disc 'probe' 90 @(96, 0, 2, 1080)
CutRect 'band_discord' $disc 'band' 90 @(400, 621, 1120, 324)
$r = @(664, 886, 184, 60)
$log = & ffmpeg -v info -y -i $b80 -vf "fps=2,format=bgra,crop=$($r[2]):$($r[3]):$($r[0]):$($r[1]),showinfo" -f rawvideo -pix_fmt bgra (Join-Path $out 'buffrow_b80.bgra') 2>&1 | Out-String
$pts = [regex]::Matches($log, 'pts_time:([0-9.]+)') | ForEach-Object { [double]$_.Groups[1].Value }
[ordered]@{ source = 'buff_80_to_110.mp4'; slot = 'buffs'; hud = 110; rect = $r; width = $r[2]; height = $r[3]; pts = @($pts) } |
  ConvertTo-Json -Depth 3 | Set-Content -Encoding utf8NoBOM (Join-Path $out 'buffrow_b80.json')
Write-Host "buffrow_b80  $($pts.Count) frame(s)"

# 발키리 Z and C (round icons under the orb, not at the 아덴1/아덴2 spots): buff_gameplay.mp4 and buff_wrong_detect.mp4
# (해방자, HUD 110), whole clips at 2 fps. Lit about 150–160 luminance in the middle of the icon, dark (not enough
# resource) about 57–60.
foreach ($spec in @(@('valk_gameplay', 'buff_gameplay.mp4'), @('valk_wrong', 'buff_wrong_detect.mp4'))) {
  foreach ($k in @(@('Z', '아덴1', @(888, 1026, 40, 43)), @('C', '아덴2', @(992, 1026, 40, 43)))) {
    $r = $k[2]; $name = "$($spec[0])_$($k[0])"
    $log = & ffmpeg -v info -y -i (Join-Path $VideoDir $spec[1]) -vf "fps=2,format=bgra,crop=$($r[2]):$($r[3]):$($r[0]):$($r[1]),showinfo" -f rawvideo -pix_fmt bgra (Join-Path $out "$name.bgra") 2>&1 | Out-String
    $pts = [regex]::Matches($log, 'pts_time:([0-9.]+)') | ForEach-Object { [double]$_.Groups[1].Value }
    [ordered]@{ source = $spec[1]; slot = $k[1]; hud = 110; rect = $r; width = $r[2]; height = $r[3]; pts = @($pts) } |
      ConvertTo-Json -Depth 3 | Set-Content -Encoding utf8NoBOM (Join-Path $out "$name.json")
    Write-Host "$name  $($pts.Count) frame(s)"
  }
}

# 직업각인 by the 초각스 icon (SubclassMatcherTests). The icons: the art inside the frame (66 px) cut from the skill-window
# shots in reference/subclass; the same cuts go to Profiles/subclass/<클래스>/<직업각인>.png for the app. The slot at 1 fps:
# buff_gameplay.mp4 (발키리 해방자, HUD 110), 4k_down.mp4 (바드 아리아, HUD 100 in the 810-high viewport), 전태.mp4 (워로드),
# 도화가support.mp4 (도화가 만개, the gold icon; 회귀 is the purple one).
$sub = Join-Path $ref 'subclass'
foreach ($i in @(@('빛의기사', '발키리\빛의 기사.PNG', 10, 9), @('해방자', '발키리\해방자.PNG', 13, 11), @('전투태세', '워로드\전태.PNG', 13, 13),
                 @('고독한기사', '워로드\고독한기사.png', 5, 5), @('절실한구원', '바드\바드.PNG', 16, 24), @('진실된용맹', '바드\바드.PNG', 16, 158),
                 @('회귀', '도화가\딜러_서폿.PNG', 14, 17, 54), @('만개', '도화가\딜러_서폿.PNG', 14, 153, 54))) {
  # 고독한기사: cut from the in-game slot; 도화가: a smaller skill window (60 px frame)
  $side = if ($i.Count -gt 4) { $i[4] } elseif ($i[0] -eq '고독한기사') { 42 } else { 66 }
  CutRect "icon_$($i[0])" (Join-Path $sub $i[1]) '초각스' 0 @($i[2], $i[3], $side, $side)
}
foreach ($spec in @(@('subclass_valk', 'buff_gameplay.mp4', 110, @(592, 992, 54, 54)), @('subclass_bard', '4k_down.mp4', 100, @(709, 885, 37, 37)),
                    @('subclass_war', '전태.mp4', 110, @(592, 992, 54, 54)),
                    @('subclass_doh', 'subclass\도화가\도화가support.mp4', 110, @(592, 992, 54, 54)))) {
  $r = $spec[3]
  $src = if ($spec[1] -like 'subclass*') { Join-Path $ref $spec[1] } else { Join-Path $VideoDir $spec[1] }
  $log = & ffmpeg -v info -y -i $src -vf "fps=1,format=bgra,crop=$($r[2]):$($r[3]):$($r[0]):$($r[1]),showinfo" -f rawvideo -pix_fmt bgra (Join-Path $out "$($spec[0]).bgra") 2>&1 | Out-String
  $pts = [regex]::Matches($log, 'pts_time:([0-9.]+)') | ForEach-Object { [double]$_.Groups[1].Value }
  [ordered]@{ source = $spec[1]; slot = '초각스'; hud = $spec[2]; rect = $r; width = $r[2]; height = $r[3]; pts = @($pts) } |
    ConvertTo-Json -Depth 3 | Set-Content -Encoding utf8NoBOM (Join-Path $out "$($spec[0]).json")
  Write-Host "$($spec[0])  $($pts.Count) frame(s)"
}

# 발키리 빛의 기사's orb gauge (GaugeLoaderTests): empty.png and full.png (1920×1080, HUD 110) cut at the mask's box, and
# the mask built from them (--build-gauge --region=924,964,71,75, then everything outside the orb's ellipse cleared).
$valk = Join-Path $sub '발키리'
CutRect 'gauge_valk_dealer_empty' (Join-Path $valk 'empty.png') 'gauge' 110 @(924, 964, 71, 75)
CutRect 'gauge_valk_dealer_full' (Join-Path $valk 'full.png') 'gauge' 110 @(924, 964, 71, 75)
CutRect 'gauge_valk_dealer_mask' (Join-Path $root 'Profiles\aden\발키리_빛의기사\mask.png') 'gauge' 110 @(0, 0, 71, 75)
# 발키리 해방자's orb (pearl white; same place and ellipse as 빛의 기사's) and 워로드's purple U around the shield (shared by
# both 직업각인; --region=913,965,95,110, then only the purple pixels kept).
CutRect 'gauge_valk_support_empty' (Join-Path $valk '해방자empty.png') 'gauge' 110 @(924, 964, 71, 75)
CutRect 'gauge_valk_support_full' (Join-Path $valk '해방자full.png') 'gauge' 110 @(924, 964, 71, 75)
CutRect 'gauge_valk_support_mask' (Join-Path $root 'Profiles\aden\발키리_해방자\mask.png') 'gauge' 110 @(0, 0, 71, 75)
$war = Join-Path $sub '워로드'
CutRect 'gauge_war_empty' (Join-Path $war 'empty전태고독한기사공용.png') 'gauge' 110 @(913, 966, 91, 103)
CutRect 'gauge_war_full' (Join-Path $war 'full전태고독한기사공용.png') 'gauge' 110 @(913, 966, 91, 103)
CutRect 'gauge_war_mask' (Join-Path $root 'Profiles\aden\워로드\mask.png') 'gauge' 110 @(0, 0, 91, 103)
# The shine the game sweeps over 해방자's orb (GaugeFilterTests): 발키리아덴버그.mp4, the whole clip at 10 fps (the engine's
# gauge rate), cut at the orb mask's box.
$bug = Join-Path $valk '발키리아덴버그.mp4'
$log = & ffmpeg -v info -y -i $bug -vf "fps=10,format=bgra,crop=71:75:924:964,showinfo" -f rawvideo -pix_fmt bgra (Join-Path $out 'gauge_valk_shine.bgra') 2>&1 | Out-String
$pts = [regex]::Matches($log, 'pts_time:([0-9.]+)') | ForEach-Object { [double]$_.Groups[1].Value }
[ordered]@{ source = '발키리아덴버그.mp4'; slot = 'gauge'; hud = 110; rect = @(924, 964, 71, 75); width = 71; height = 75; pts = @($pts) } |
  ConvertTo-Json -Depth 3 | Set-Content -Encoding utf8NoBOM (Join-Path $out 'gauge_valk_shine.json')
# 발키리아덴버그2.mp4: shines on a low gauge (3.3 s, 7.3 s) and on a full one (11.5 s); the whole clip at 10 fps.
$bug2 = Join-Path $valk '발키리아덴버그2.mp4'
$log = & ffmpeg -v info -y -i $bug2 -vf "fps=10,format=bgra,crop=71:75:924:964,showinfo" -f rawvideo -pix_fmt bgra (Join-Path $out 'gauge_valk_shine2.bgra') 2>&1 | Out-String
$pts = [regex]::Matches($log, 'pts_time:([0-9.]+)') | ForEach-Object { [double]$_.Groups[1].Value }
[ordered]@{ source = '발키리아덴버그2.mp4'; slot = 'gauge'; hud = 110; rect = @(924, 964, 71, 75); width = 71; height = 75; pts = @($pts) } |
  ConvertTo-Json -Depth 3 | Set-Content -Encoding utf8NoBOM (Join-Path $out 'gauge_valk_shine2.json')

# 바드's crystals (GaugeLoaderTests): 절실한구원 has four, 진실된용맹 three, each filling bottom to top, left to right
# (--build-gauge --region=918,925,84,120, then only the blue crystal pixels kept and cut to them).
$bard = Join-Path $sub '바드'
foreach ($spec in @(@('gauge_bard_support', '절실한구원', @(925, 979, 69, 66)), @('gauge_bard_dealer', '진실된용맹', @(930, 979, 58, 66)))) {
  $r = $spec[2]
  CutRect "$($spec[0])_empty" (Join-Path $bard "$($spec[1])empty.png") 'gauge' 110 $r
  CutRect "$($spec[0])_full" (Join-Path $bard "$($spec[1])full.png") 'gauge' 110 $r
  CutRect "$($spec[0])_mask" (Join-Path $root "Profiles\aden\바드_$($spec[1])\mask.png") 'gauge' 110 @(0, 0, $r[2], $r[3])
}

# 워로드 전투태세's C (아덴2, case 2, HUD 110) at 10 fps: 전태 버그 아덴키.mp4 (gauge full and C lit, cast at about
# 1.5 s, then 1m) and 전태.mp4 (dark 0–1 s and 32–36 s, lit 2–15 s, then 1m and seconds).
foreach ($spec in @(@('war_bug_C', '전태 버그 아덴키.mp4'), @('war_C', '전태.mp4'))) {
  $r = Rect '아덴2' 110; $name = $spec[0]
  $log = & ffmpeg -v info -y -i (Join-Path $VideoDir $spec[1]) -vf "fps=10,format=bgra,crop=$($r[2]):$($r[3]):$($r[0]):$($r[1]),showinfo" -f rawvideo -pix_fmt bgra (Join-Path $out "$name.bgra") 2>&1 | Out-String
  $pts = [regex]::Matches($log, 'pts_time:([0-9.]+)') | ForEach-Object { [double]$_.Groups[1].Value }
  [ordered]@{ source = $spec[1]; slot = '아덴2'; hud = 110; rect = $r; width = $r[2]; height = $r[3]; pts = @($pts) } |
    ConvertTo-Json -Depth 3 | Set-Content -Encoding utf8NoBOM (Join-Path $out "$name.json")
  Write-Host "$name  $($pts.Count) frame(s)"
}

# 도화가 (shared by both 직업각인): three orbs, each a blue ring filling clockwise from the bottom, then gold once full.
# The mask is the ring's band (12–15.5 px round each ring's middle, found on 도화가support.mp4) in the full shot's gold.
# 도화가support.mp4 at 10 fps: the gauge, and Z (아덴1) and C (아덴2), which go dark on an empty gauge.
$doh = Join-Path $sub '도화가'
$g = @(898, 1024, 125, 37)
CutRect 'gauge_doh_empty' (Join-Path $doh 'aden empty for both.png') 'gauge' 110 $g
CutRect 'gauge_doh_full' (Join-Path $doh 'aden full for both.png') 'gauge' 110 $g
CutRect 'gauge_doh_mask' (Join-Path $root 'Profiles\aden\도화가\mask.png') 'gauge' 110 @(0, 0, $g[2], $g[3])
foreach ($spec in @(@('gauge_doh_play', 'gauge', $g), @('doh_Z', '아덴1', (Rect '아덴1' 110)), @('doh_C', '아덴2', (Rect '아덴2' 110)))) {
  $name = $spec[0]; $r = $spec[2]
  $log = & ffmpeg -v info -y -i (Join-Path $doh '도화가support.mp4') -vf "fps=10,format=bgra,crop=$($r[2]):$($r[3]):$($r[0]):$($r[1]),showinfo" -f rawvideo -pix_fmt bgra (Join-Path $out "$name.bgra") 2>&1 | Out-String
  $pts = [regex]::Matches($log, 'pts_time:([0-9.]+)') | ForEach-Object { [double]$_.Groups[1].Value }
  [ordered]@{ source = '도화가support.mp4'; slot = $spec[1]; hud = 110; rect = $r; width = $r[2]; height = $r[3]; pts = @($pts) } |
    ConvertTo-Json -Depth 3 | Set-Content -Encoding utf8NoBOM (Join-Path $out "$name.json")
  Write-Host "$name  $($pts.Count) frame(s)"
}
# 도화가bug_aden.mp4 (same setup) at 10 fps: the gauge, the 1st orb filling and turning gold, then C and Z spending.
foreach ($spec in @(@('gauge_dohbug', 'gauge', $g), @('dohbug_Z', '아덴1', (Rect '아덴1' 110)), @('dohbug_C', '아덴2', (Rect '아덴2' 110)))) {
  $name = $spec[0]; $r = $spec[2]
  $log = & ffmpeg -v info -y -i (Join-Path $ref '도화가bug_aden.mp4') -vf "fps=10,format=bgra,crop=$($r[2]):$($r[3]):$($r[0]):$($r[1]),showinfo" -f rawvideo -pix_fmt bgra (Join-Path $out "$name.bgra") 2>&1 | Out-String
  $pts = [regex]::Matches($log, 'pts_time:([0-9.]+)') | ForEach-Object { [double]$_.Groups[1].Value }
  [ordered]@{ source = '도화가bug_aden.mp4'; slot = $spec[1]; hud = 110; rect = $r; width = $r[2]; height = $r[3]; pts = @($pts) } |
    ConvertTo-Json -Depth 3 | Set-Content -Encoding utf8NoBOM (Join-Path $out "$name.json")
  Write-Host "$name  $($pts.Count) frame(s)"
}
# 도화가zc.png (만개, gauge full, Z and X lit): the bottom strip, for the fit, the gauge and the 아덴 slots.
CutRect 'dohzc_bottom' (Join-Path $ref '도화가zc.png') 'band' 0 @(0, 900, 1920, 180)

# The class from the skill bar's centre piece (ClassEmblemTests): Geometry.RefEmblem cut from each gauge class's empty and
# full shots (HUD 110), also written to Profiles/aden/<folder>/emblem_empty.png and emblem_full.png for the app, and 1 fps
# through other recordings of each class (4k_down.mp4: HUD 100 in the 810-high viewport, bottom row 945).
$e110 = RefRect @(912, 962, 96, 108) 110
foreach ($s in @(@('워로드', '워로드_', '워로드\empty전태고독한기사공용.png', '워로드\full전태고독한기사공용.png'),
                 @('발키리_빛의기사', '발키리_', '발키리\empty.png', '발키리\full.png'),
                 @('발키리_해방자', '발키리_해방자', '발키리\해방자empty.png', '발키리\해방자full.png'),
                 @('바드_절실한구원', '바드_절실', '바드\절실한구원empty.png', '바드\절실한구원full.png'),
                 @('바드_진실된용맹', '바드_진실', '바드\진실된용맹empty.png', '바드\진실된용맹full.png'),
                 @('도화가', '도화가_', '도화가\aden empty for both.png', '도화가\aden full for both.png'))) {
  foreach ($state in @(@('empty', $s[2]), @('full', $s[3]))) {
    $src = Join-Path $sub $state[1]
    CutRect "emblemref_$($s[1])$($state[0])" $src 'emblem' 110 $e110
    & ffmpeg -v error -y -i $src -vf "crop=$($e110[2]):$($e110[3]):$($e110[0]):$($e110[1])" (Join-Path $root "Profiles\aden\$($s[0])\emblem_$($state[0]).png")
  }
}
foreach ($s in @(@('워로드_전태', (Join-Path $ref '전태.mp4')), @('워로드_아덴키', (Join-Path $ref '전태 버그 아덴키.mp4')),
                 @('발키리_buff', (Join-Path $VideoDir 'buff_gameplay.mp4')), @('발키리_bug', (Join-Path $sub '발키리\발키리아덴버그.mp4')),
                 @('바드_bug', (Join-Path $ref 'bard bug.mp4')), @('바드_play', (Join-Path $sub '바드\절실한구원gameplay.mp4')),
                 @('도화가_bug', (Join-Path $ref '도화가bug_aden.mp4')), @('도화가_support', (Join-Path $sub '도화가\도화가support.mp4')),
                 @('바드_4k', (Join-Path $VideoDir '4k_down.mp4'), 100, (RefRect @(912, 962, 96, 108) 100 945 0.75)),
                 @('바드_charged', (Join-Path $ref 'bardbug_103.mp4'), 100, (RefRect @(912, 962, 96, 108) 100)))) {
  $hud = $s[2] ?? 110
  $r = $s[3] ?? $e110
  $log = & ffmpeg -v info -y -i $s[1] -vf "fps=1,format=bgra,crop=$($r[2]):$($r[3]):$($r[0]):$($r[1]),showinfo" -f rawvideo -pix_fmt bgra (Join-Path $out "emblemplay_$($s[0]).bgra") 2>&1 | Out-String
  $pts = [regex]::Matches($log, 'pts_time:([0-9.]+)') | ForEach-Object { [double]$_.Groups[1].Value }
  [ordered]@{ source = (Split-Path $s[1] -Leaf); slot = 'emblem'; hud = $hud; rect = $r; width = $r[2]; height = $r[3]; pts = @($pts) } |
    ConvertTo-Json -Depth 3 | Set-Content -Encoding utf8NoBOM (Join-Path $out "emblemplay_$($s[0]).json")
  Write-Host "emblemplay_$($s[0])  $($pts.Count) frame(s)"
}

# 모르둠 debuffs (PLAN §4 디버프): 1920×1080, HUD 110, buff 120%. mordum.png is the whole screen; the "mordum debuff*.png"
# shots are crops of it at (453, 721), (551, 741) and (657, 755) (found by matching the class emblem). The debuff row,
# cut at screen (1060, 880, 200, 60) in each: 0 blue sigil; 1 blue, lightning "6초"; 2 blue, swap "3초"; 3 a yellow
# burst "5초", a red figure "8초", red sigil, lightning "6초". The boss's own row under its HP bar (screen 700, 140, 540,
# 70): the red and blue sigils with their "X 4" counts, then its timed debuffs. The buff stacks "X 10", "X 6" (debuff
# 1) and "X 8" (debuff 2) are the same font as the boss counts.
$m = @('mordum.png', 'mordum debuff.png', 'mordum debuff2.png', 'mordum debuff3.png')
$at = @(@(0, 0), @(453, 721), @(551, 741), @(657, 755))
for ($i = 0; $i -lt 4; $i++) { CutRect "debuffrow_$i" (Join-Path $ref $m[$i]) 'debuffs' 110 @((1060 - $at[$i][0]), (880 - $at[$i][1]), 200, 60) }
CutRect 'bossrow' (Join-Path $ref $m[0]) 'boss' 110 @(700, 140, 540, 70)
# 결투자.png: a 143×81 crop of a debuff row (HUD 110, buff about 110%: the frames are 29 px, not 32). Left the blue sigil, right
# the 결투자 burst at (71, 30, 29, 29), the game's own "38초" under it.
CutRect 'duelist' (Join-Path $ref '결투자.png') 'debuffs' 110 @(0, 0, 143, 81)
CutRect 'stacks_1' (Join-Path $ref $m[1]) 'stacks' 110 @(60, 150, 200, 22)
CutRect 'stacks_2' (Join-Path $ref $m[2]) 'stacks' 110 @(50, 128, 200, 22)

# 18-22.mp4 (the same settings, 60 fps), single frames: 19:19 blue sigil and swap "0.4초" (the swap's red and blue
# halves average to no stable hue); the boss's counts "X 5" / "X 3" at 21:05 and "X 6" / "X 2" at 21:37.5.
$v1822 = Join-Path $ref '18-22.mp4'
CutRect 'debuffrow_4' $v1822 'debuffs' 110 @(1060, 880, 200, 60) 1159 0.016
# debuffrow_5: the 2 fps sample at 20:15 (frame 270 of fps=2 from 18:00, as a 2 fps probe read it): the red sigil alone,
# whose empty neighbour once passed as the swap. Its .json (pts 1215.01) is kept by hand.
& ffmpeg -v error -y -ss 1080 -t 136 -i $v1822 -vf 'fps=2,format=bgra,crop=200:60:1060:880,select=eq(n\,270)' -frames:v 1 -f rawvideo (Join-Path $out 'debuffrow_5.bgra')
CutRect 'bossrow_53' $v1822 'boss' 110 @(700, 140, 540, 70) 1265 0.016
CutRect 'bossrow_62' $v1822 'boss' 110 @(700, 140, 540, 70) 1297.5 0.016
# The same 2 fps samples of the boss's row: frame 445 (21:42.5) "X 6" / "X 2" over a brighter background, and frame 460
# (21:50) "X 6" over red and a bare blue sigil (one stack shows no count). Their .json files are kept by hand.
foreach ($f in @(@('bossrow_62b', 445), @('bossrow_61', 460))) {
  & ffmpeg -v error -y -ss 1080 -t 240 -i $v1822 -vf "fps=2,format=bgra,crop=540:70:700:140,select=eq(n\,$($f[1]))" -frames:v 1 -f rawvideo (Join-Path $out "$($f[0]).bgra")
}

# sBug.mp4 (HUD 110): S (일반6) counting 2s → 1s → ready on its bright blue icon; "1s" was read as 10.
Cut 'sbug_일반6' (Join-Path $ref 'sBug.mp4') '일반6' 110 9.0 2.8
# chainSkill.mp4 (HUD 110): a chain skill's use flashes white at the edge, an orange rim pulses while its next part is
# ready, then the icon looks ready with no number until the cooldown shows (W: flashes 1.67 and 2.13 s, "4s" at 3.1 s).
$chain = Join-Path $ref 'chainSkill.mp4'
Cut 'chain_W_1' $chain '일반2' 110 1.4 2.2
Cut 'chain_W_2' $chain '일반2' 110 7.0 2.0
Cut 'chain_Q' $chain '일반1' 110 13.2 1.6

# 단심 (단죄/심판 rune; 1080p, HUD 110, buff 120% like buff_gameplay). The buff row's right end, boxes 0–3: 심판 replaces
# 단죄 in box 0 at 48.2 s ("5초", "4초" from 49.15 s) to the clip's end (53.4 s); 10 fps.
# 단죄여려번.mp4: 단죄 many times and an unrelated blue spear buff (~8 s) beside it, no 심판; 4 fps.
function CutFps($name, $src, $slot, $hud, $r, $fps, $start = $null, $duration = $null) {
  $ffArgs = @('-v', 'info', '-y')
  if ($null -ne $start) { $ffArgs += @('-ss', "$start") }
  if ($null -ne $duration) { $ffArgs += @('-t', "$duration") }
  $ffArgs += @('-i', $src, '-vf', "fps=$fps,format=bgra,crop=$($r[2]):$($r[3]):$($r[0]):$($r[1]),showinfo", '-f', 'rawvideo', '-pix_fmt', 'bgra', (Join-Path $out "$name.bgra"))
  $log = & ffmpeg @ffArgs 2>&1 | Out-String
  $pts = [regex]::Matches($log, 'pts_time:([0-9.]+)') | ForEach-Object { [double]$_.Groups[1].Value + [double]($start ?? 0) }
  [ordered]@{ source = (Split-Path $src -Leaf); slot = $slot; hud = $hud; rect = $r; width = $r[2]; height = $r[3]; pts = @($pts) } |
    ConvertTo-Json -Depth 3 | Set-Content -Encoding utf8NoBOM (Join-Path $out "$name.json")
  Write-Host "$name  $($pts.Count) frame(s)  rect $($r -join ',')"
}
$jd = Join-Path $ref '단죄 폰트 변경 + 심판 효과 at 100%.mp4'
$dz = Join-Path $ref '단죄여려번.mp4'
CutFps 'judg_row' $jd 'buffs' 110 @(700, 886, 150, 60) 10 47.5 8.0
CutFps 'dz_row' $dz 'buffs' 110 @(700, 886, 150, 60) 4
# The text over the character (the player's orange bar near (923, 373), 440×300 under it). 심판 fades in at 48.18 s, large,
# and settles by 48.4 s; 10 fps. Single frames of 단죄 at each 데미지 폰트 size the clip switches to, and 슈퍼차지.
$tb = @(740, 320, 440, 300)
CutFps 'judg_text' $jd 'text' 110 $tb 10 48.0 1.6
foreach ($f in @(@('dz_text_100a', 2.24), @('dz_text_200b', 15.41), @('dz_text_150', 15.92), @('dz_text_200', 24.51),
    @('dz_text_300a', 37.0), @('dz_text_300b', 37.25), @('dz_text_100b', 46.69), @('super_text', 47.69))) {
  CutRect $f[0] $jd 'text' 110 $tb $f[1] 0.016
}
# 단심75%.mp4 … 단심300%.mp4 (the same setup, one 심판 each at that 데미지 폰트 size): 1.6 s round it at 5 fps, and 단죄 at 75%.
foreach ($f in @(@('judg_text_75', '단심75%.mp4', 2.0), @('judg_text_150', '단심150%.mp4', 2.0), @('judg_text_200', '단심 200%.mp4', 2.3),
    @('judg_text_300', '단심300%.mp4', 2.8))) {
  CutFps $f[0] (Join-Path $ref $f[1]) 'text' 110 $tb 5 $f[2] 1.6
}
CutRect 'dz_text_75' (Join-Path $ref '단심75%.mp4') 'text' 110 $tb 1.25 0.016
# Busy combat round the character (damage numbers at every size, 헤드 어택, 65, effects), no 심판: negatives.
foreach ($f in @(@('busy_300a', '단심300%.mp4', 0.6), @('busy_300b', '단심300%.mp4', 1.2), @('busy_200', '단심 200%.mp4', 0.8),
    @('busy_150', '단심150%.mp4', 1.0), @('busy_100a', '단죄 폰트 변경 + 심판 효과 at 100%.mp4', 33.6), @('busy_100b', '단죄 폰트 변경 + 심판 효과 at 100%.mp4', 34.8))) {
  CutRect $f[0] (Join-Path $ref $f[1]) 'text' 110 $tb $f[2] 0.016
}
