# 아덴 게이지 정의

Each class has its own gauge shape, size and type: bar, arc, ring, orbs/stacks. So a gauge is not described by a fixed shape. It is described by **its own pixels** and **the order in which they fill**. One reader handles every shape (`GaugeReader`, loaded by `GaugeLoader`).

A gauge is switched on by two things together:

1. `"adenGauge"` on the 직업각인 in `../classes.json`: `true` for a plain bar, or an object with the bar's splits and glow rule (see the `_note` there). `null` or missing = no gauge.
2. A definition in this folder:
   - `<클래스>_<직업각인>/` if the two 직업각인 have different gauges, otherwise
   - `<클래스>/`, shared by both.

If `adenGauge` is set but no definition exists, the settings window shows "게이지 정의 없음" and the gauge is not read. A broken definition is logged and skipped the same way.

The engine reads the gauge every 100 ms, and every frame for 0.1 s after a cast is seen on screen (`GaugePace`), while it is shown or sent to the party, and not while the skill bar is hidden (loading screens, cutscenes).

## Adding a gauge

1. Take two screenshots at 1920×1080, HUD 110%, with nothing else changing if you can: `empty.png` and `full.png`, in one folder (e.g. `reference/aden/<클래스>[_<직업각인>]/`). Frames from a recording at that size work too.
2. Run `GaugeOverlay.exe --build-gauge <스크린샷 폴더> <출력 폴더> --region=x,y,w,h`, with the output folder `Profiles/aden/<클래스>[_<직업각인>]` and the region a box around the gauge in reference pixels. Without `--region` it searches the class emblem and 40 px around it, which usually also catches the 아덴 skill icons and the background showing through the HUD, so give the region. It:
   - compares `empty` and `full` inside the region: the pixels that change are the gauge;
   - saves `mask.png` (the gauge pixels in their filled colour, the rest transparent), cut to the gauge's bounding box;
   - writes `gauge.json` with the mask's position, `pieces` if the mask is several separate parts (else `continuous`), left→right order and the tolerance.
3. Check `mask.png`: erase any stray specks (make them transparent) so only the gauge is left, then fix `units` and `order` in `gauge.json` if needed.
4. Set `adenGauge` in `classes.json`.

## gauge.json

Coordinates are in **reference pixels**: the 1920×1080 screen at HUD 110%. The app places them with the same fit as the skill slots, so the gauge scales with resolution, HUD 크기 and 21:9, and a capture of any size is scaled back to the mask's size before reading.

```json
{
  "origin": [920, 963],
  "units": "continuous",
  "order": { "kind": "linear", "angleDeg": 90 },
  "tolerance": 40
}
```

| field | meaning |
|---|---|
| `origin` | top-left corner of `mask.png` in reference pixels (written by `--build-gauge`) |
| `units` | `continuous` = read as 0–100%; `pieces` = the share of lit pieces (orbs/stacks), each separate part of the mask being one |
| `order` | for `continuous`, how the pixels fill. `linear`: along `angleDeg`, 0 = left→right (default), 90 = bottom→top, 180 = right→left. `angular`: around `center` [x, y] (mask pixels), from `startDeg` (0 = right, 90 = up, default 90), `clockwise` (default true); for rings and arcs. `piecewise`: separate parts filled one after another (바드's crystals), left→right, each along `angleDeg` (default 90 = bottom→top). `rings`: separate rings filled one after another (도화가's orbs), left→right, each around its own middle from `startDeg` (default 90), `clockwise` (default true). |
| `filling` | optional list of `"#RRGGBB"` colours that also count as filled at any gauge pixel, for a gauge that fills in one colour and turns another when done (바드's crystals fill gold and turn blue; the mask holds the blue, `filling` the gold shades) |
| `slack` | optional, 0–10 px (default 0): how far across the fill direction a pixel may match its own filled colour, for a gauge so thin that the fit's error of a few pixels misses it (바드's 8 px crystals: 3). Only on the same piece of the mask and only for colours well away from grey, so the frame and skill icons beside the gauge don't count |
| `tolerance` | how far (per RGB channel) a pixel may be from its stored filled colour and still count as filled (default 40) |

**How the value is read:** `continuous` splits the fill order into 100 steps and gives the end of the furthest step whose pixels mostly match their filled colour (or an `filling` one); `pieces` counts the pieces whose pixels mostly match. Each gauge's colours and shape come from its own screenshots, so nothing in the reader is tuned per class.

**Overlay:** whatever the in-game shape, the overlay shows the value as the horizontal bar at the top of the box, with the colour, empty colour and thickness you set. Split marks, segment colours and the glow come from `adenGauge` and the 게이지 tab.

## Adding a class: questions to ask first

Ask the user these before adding a class or 직업각인. The answers decide the data in `classes.json`, not code.

1. **Each 아덴 skill's mechanic** (per 직업각인, per slot):
   - **Case 1: plain cooldown.** Used, then a timer (e.g. 고독한기사's C). Read like any skill.
   - **Case 2: cooldown plus a resource.** Off cooldown but unusable (dark) without enough gauge (e.g. 전투태세's C, which needs 50%; reference: `reference/전태.mp4`). Needs the lit/dark reading (`adenLit`) and the gauge rule.
   - **Case 3: cooldown with stacks.** Several uses, each recharging (a stack count on the icon).
   - Also: does the icon show digits while unavailable, or only lit/dark (발키리 Z/C show no digits)?
   - **How does the game show that the skill is usable (active)?** It differs per class, so ask every time. 발키리 Z/C and 도화가 Z/C: the icon goes from dark to lit (도화가: under one full orb both are dark). 워로드 전투태세 C: a faint purple outline around the slot, and the key label (C) turns white; not usable, the label is grey and the icon dark (전태.mp4, 전태 버그 아덴키.mp4). Only 워로드 has the purple outline.
2. **Can the 직업각인 be told apart on screen?** Detection uses the 초각스 icon colour. Ask for a clean shot of each 직업각인's 초각스 icon off cooldown, and whether any two look alike (then detection will ask the user instead of guessing).
3. **Gauge:** does each 직업각인 have one, and its splits (see `adenGauge` in `classes.json`), with empty and full screenshots. Ask whether the gauge changes colour when a part is done: 바드's crystal fills in gold and turns blue once full, so the mask is the blue and the gold shades go in `filling` (measured in 절실한구원gameplay.mp4). The overlay bar copies it by default through `"colors": { "full", "filling" }` in `adenGauge` (바드: `#0A84E0` blue, `#D7B932` gold): the section still filling is drawn in `filling`, full ones in `full` (the ramp deepens from it), and a section turning full eases from one to the other over 0.4 s. The user can change both per 클래스·직업각인 in the 게이지 tab. 도화가: a blue ring round each orb fills clockwise from the bottom while the section fills, and the whole orb turns gold once full; the mask is the ring's band (12–15.5 px round each ring's middle, fitted on 도화가support.mp4) in the gold, the ring's blues go in `filling`, and the order is `rings` (the overlay: `#F5BE50` gold, `#6464DC` blue).
4. **When does the gauge glow?** Ask per 직업각인; the answer differs by class. The glow is drawn as one span across the segments. Known answers so far:
   - 바드 (both): each section glows once it is full (`"when": "sections"`).
   - 도화가 (both): each orb glows once it is full (`"when": "sections"`).
   - 전투태세: the whole fill glows while C is lit and the gauge is at least 50% (`slotReady`, 아덴2, 0.5).
   - 해방자: the whole fill glows from 80% while Z is ready (`slotReady`, 아덴1, 0.8). 빛의기사: at a full gauge while C is ready (`slotReady`, 아덴2, 1).
   - 고독한기사: no glow.
   Also ask whether the glow needs the skill ready or only the gauge value; a rule the two existing kinds can't express needs a new `glow.when`.
5. **Does the fill deepen as sections fill?** (`"ramp"` in `adenGauge`: the colour level, 1 weak · 2 medium · 3 strong, for 1, 2, … full sections; past the list the last level holds; below one full section the normal colour.) Known answers so far:
   - 절실한구원: weak at 1, medium at 2, strong at 3; the 4th changes nothing (`[1, 2, 3]`).
   - 진실된용맹: medium at 1, strong at 2 and more (`[2, 3]`).
   - 도화가 (both): medium at 1, strong at 2 and more (`[2, 3]`).
6. **Which skills spend the gauge, and how much?** (`"spend"` in `adenGauge`.) Pressing the skill's key works out what a use would leave: the full sections, lowest first and at most `max`, or a fixed share with `"amount"`. For 1 s after the press the gauge is read every frame, and two reads in a row within half the narrowest section of that (0.1 for a share) confirm the use: the drop shows at once, and for 0.5 s after, reads above it are ignored (the skill's effects over the gauge). A press that doesn't cast finds no drop and changes nothing (`GaugeFilter.Arm`). With the key not set in the app, the skill's ready → cooldown turn arms it the same way. How soon a rise shows is the 게이지 읽기 setting (PLAN.md, 아덴 게이지 bar). Known answers so far:
   - 바드: Z or X spends the full crystals, 절실한구원 at most 3 of 4 (bard bug.mp4: 4 full, one kept), 진실된용맹 at most 2 of 3.
   - 도화가 (both): Z spends 1 or 2 full orbs, C at most 1 (1.5 orbs full: C takes 1 and leaves the half). Different maxima per skill are a list: `"spend": [{ "slots": ["아덴1"], "max": 2 }, { "slots": ["아덴2"], "max": 1 }]`.
   - 워로드: 전투태세 spends 50% (`"amount": 0.5`, on its C), 고독한기사 nothing.
   - 발키리: 80% (`"amount": 0.8`), on Z for both 직업각인.
7. **Role** (딜러/서포터) of each 직업각인.
