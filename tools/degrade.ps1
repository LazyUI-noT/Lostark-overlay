# 테스트 모드 강화 읽기 (reinforced-reader.plan.md): damaged copies of a 1080p clip, as streams and recordings show the game.
# Writes <name>_720p / _lowrate / _1440to1080 / _drops .mp4 under reference/degraded (git-ignored). Requires ffmpeg on PATH.
#   720p        down to 1280×720 and back up (a 720p stream watched full-screen)
#   lowrate     H.264 at CRF 35 (blocky digits, smeared edges; ~1.2 Mbps, a stress test)
#   stream4m    4 Mbps (a stream; Twitch 1080p60 is ~6)
#   1440to1080  up to 2560×1440, then down to 1080p (a bigger screen's stream at 1080p; the HUD lands between sizes)
#   drops       60 → 24 → 60 fps (frames repeated and missing, as on a stream)
param([Parameter(Mandatory)][string[]]$Clips, [string]$OutDir)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutDir) { $OutDir = Join-Path $root 'reference/degraded' }
New-Item -ItemType Directory -Force $OutDir | Out-Null

$kinds = [ordered]@{
  '720p'       = @('-vf', 'scale=1280:720:flags=bilinear,scale=1920:1080:flags=bilinear', '-crf', '23')
  'lowrate'    = @('-crf', '35')                                       # ~1.2 Mbps: a stress test, far under any stream
  'stream4m'   = @('-b:v', '4M', '-maxrate', '4M', '-bufsize', '8M')   # under a Twitch 1080p60 stream's ~6 Mbps
  '1440to1080' = @('-vf', 'scale=2560:1440:flags=bicubic,scale=1920:1080:flags=area', '-crf', '23')
  'drops'      = @('-vf', 'fps=24,fps=60', '-crf', '23')
}
foreach ($clip in $Clips) {
  $name = [IO.Path]::GetFileNameWithoutExtension($clip)
  foreach ($k in $kinds.Keys) {
    $out = Join-Path $OutDir "${name}_$k.mp4"
    if (Test-Path $out) { continue }
    & ffmpeg -v error -y -i $clip @($kinds[$k]) -c:v libx264 -preset veryfast -an $out
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed: $out" }
    Write-Host $out
  }
}
