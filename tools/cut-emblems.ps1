# Cuts the class emblems out of reference/emblems*.PNG and reference/<클래스>.PNG (screenshots of a class list: a
# black emblem left of each class name, on white) into Profiles/emblems/<클래스>.png: the emblem in white with a small
# dark shadow to the bottom right, on transparent, for the dark overlay.
# Each screenshot lists its classes top to bottom in the order given below.
param([string]$root = (Split-Path $PSScriptRoot))
Add-Type -AssemblyName System.Drawing

$lists = [ordered]@{
    'emblems'  = '슬레이어', '워로드', '발키리', '홀리나이트', '디스트로이어'
    'emblems2' = '도화가', '기상술사', '환수사', '차원술사'
    'emblems3' = '브레이커', '창술사', '인파이터', '배틀마스터', '스트라이커', '기공사'
    'emblems4' = '소울이터', '블레이드', '리퍼', '데모닉'
    'emblems5' = '바드', '소서리스', '아르카나', '서머너'
    'emblems6' = '건슬링어', '스카우터', '데빌헌터', '호크아이', '블래스터'
    'emblems7' = , '가디언나이트'
    '버서커'   = , '버서커'
}
$iconColumn = 56      # the emblems sit left of this x; the class names start right of it
$dark = 140           # a pixel darker than this belongs to an emblem (separator lines are light grey)
$maxGap = 6           # blank rows allowed inside one emblem
$shadowOffset = 2     # px, to the right and down
$shadowAlpha = 0.8    # of the emblem's own alpha, after a 3×3 blur

$out = Join-Path $root 'Profiles\emblems'
New-Item -ItemType Directory -Force $out | Out-Null
foreach ($file in $lists.Keys) {
    $src = [System.Drawing.Bitmap]::new((Join-Path $root "reference\$file.PNG"))
    $cols = [Math]::Min($iconColumn, $src.Width)
    $ink = [double[,]]::new($src.Width, $src.Height)   # 0 = paper, 1 = black
    for ($y = 0; $y -lt $src.Height; $y++) {
        for ($x = 0; $x -lt $cols; $x++) {
            $c = $src.GetPixel($x, $y)
            $ink[$x, $y] = [Math]::Clamp(1 - ($c.R + $c.G + $c.B) / 765, 0, 1)
        }
    }
    $isInk = { param($x, $y) $ink[$x, $y] -gt 1 - $dark / 255 }

    # Runs of rows with ink in the icon column, merged across small gaps.
    $runs = @(); $start = -1; $last = -1
    for ($y = 0; $y -lt $src.Height; $y++) {
        $hit = $false
        for ($x = 0; $x -lt $cols -and -not $hit; $x++) { $hit = & $isInk $x $y }
        if (-not $hit) { continue }
        if ($start -ge 0 -and $y - $last -gt $maxGap) { $runs += , @($start, $last); $start = -1 }
        if ($start -lt 0) { $start = $y }
        $last = $y
    }
    if ($start -ge 0) { $runs += , @($start, $last) }
    $runs = @($runs | Where-Object { $_[1] - $_[0] -ge 15 })
    $names = @($lists[$file])
    if ($runs.Count -ne $names.Count) { throw "$file`: found $($runs.Count) emblems, expected $($names.Count)" }

    for ($i = 0; $i -lt $runs.Count; $i++) {
        $top, $bottom = $runs[$i]
        $left = $cols; $right = 0
        for ($y = $top; $y -le $bottom; $y++) {
            for ($x = 0; $x -lt $cols; $x++) {
                if (& $isInk $x $y) { $left = [Math]::Min($left, $x); $right = [Math]::Max($right, $x) }
            }
        }
        $w = $right - $left + 1; $h = $bottom - $top + 1
        # Square canvas with room for the shadow; the emblem is centred ignoring the shadow.
        $side = [Math]::Max($w, $h) + 2 * ($shadowOffset + 1)
        $ox = [int](($side - $w) / 2); $oy = [int](($side - $h) / 2)
        $a = [double[,]]::new($side, $side)
        for ($y = 0; $y -lt $h; $y++) { for ($x = 0; $x -lt $w; $x++) { $a[($ox + $x), ($oy + $y)] = $ink[($left + $x), ($top + $y)] } }

        $dst = [System.Drawing.Bitmap]::new($side, $side, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        for ($y = 0; $y -lt $side; $y++) {
            for ($x = 0; $x -lt $side; $x++) {
                # Shadow: the emblem's alpha shifted and box-blurred.
                $sum = 0.0
                for ($dy = -1; $dy -le 1; $dy++) {
                    for ($dx = -1; $dx -le 1; $dx++) {
                        $sx = $x - $shadowOffset + $dx; $sy = $y - $shadowOffset + $dy
                        if ($sx -ge 0 -and $sy -ge 0 -and $sx -lt $side -and $sy -lt $side) { $sum += $a[$sx, $sy] }
                    }
                }
                $s = $shadowAlpha * $sum / 9
                $e = $a[$x, $y]
                # White emblem over black shadow.
                $outA = $e + $s * (1 - $e)
                if ($outA -le 0) { continue }
                $v = [int][Math]::Round(255 * $e / $outA)
                $dst.SetPixel($x, $y, [System.Drawing.Color]::FromArgb([int][Math]::Round(255 * $outA), $v, $v, $v))
            }
        }
        $dst.Save((Join-Path $out "$($names[$i]).png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $dst.Dispose()
        "{0,-8} {1}x{2}" -f $names[$i], $w, $h
    }
    $src.Dispose()
}
