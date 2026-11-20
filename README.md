# Upper GI Bleed

An endoscopic haemostasis trainer with fictional patients. Find the bleed, pick the right attachment, control it, wash and re-inspect, sign off. Score points and earn coffee tokens; go from case to case until you quit or the patient dies.

**Play it:** https://brendanjvr-ux.github.io/duodenal-bleed/ (Unity WebGL build at `/play/`; the original canvas prototype at `/web/`)

## The Unity game (`UpperGIBleed/`)

Unity 6 (6000.6.x), URP. Everything is built from code: the scene has one `Bootstrap` object, and the menus, HUD, world and effects are created at runtime, so there are no hand-edited prefabs.

| Menu | What it does |
|---|---|
| New case | Asks for a name and avatar the first time, then starts a run: case 1 (oesophageal varices), 2 (gastric ulcer), 3 (duodenal ulcer), then round again with brisker bleeding. |
| Leaderboard | Best runs on this device: score, coffee tokens, cases cleared, how the run ended. |
| Settings | Volumes, scope speed, screen effects, on-screen pad, clear leaderboard. |
| Quit | Closes the app (in a browser it can only ask you to close the tab). |

**Scoring.** Each case is worth 0-1000: up to 550 for speed (full marks until the case's par time, none at twice par) and up to 450 for blood loss (full marks at no loss, none at the 1500 mL decompensation limit). An unverified sign-off keeps 60%. A fail (signed off while still bleeding, or the patient decompensated) scores 0, pays 0 coffee tokens and ends the run. Otherwise 1-10 coffee tokens, one per 100 points. Tokens bank on your profile across runs. Formula in `Assets/Scripts/Scoring.cs`.

**Controls.** ↑/↓ scope forward/back · ←/→ rotate the instrument tip round the wall · Tab next attachment (Shift+Tab back) · Space use (hold for wash and cautery, tap for a band) · Enter sign off · P pause · N next case from the debrief. Touch devices get an on-screen pad.

### Art from Blender

`art/blender/tract.py` builds the tract (oesophagus → Z-line → stomach with rugae and the incisura → antrum → pyloric rosette → D1) as one mesh with relief, bakes the mucosa texture, and makes the props (ulcer, varix, band, tools) and the six avatars:

```bash
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python art/blender/tract.py -- UpperGIBleed/Assets/Resources/Art
```

### Headless commands

The project has no hand-made scene; the Editor builds it. With `U=/Applications/Unity/Hub/Editor/6000.6.10f1/Unity.app/Contents/MacOS/Unity` and `P=$PWD/UpperGIBleed`:

```bash
$U -batchmode -nographics -quit -projectPath $P -executeMethod SceneBuilder.Build -logFile -
```

```bash
$U -batchmode -nographics -quit -projectPath $P -executeMethod SelfTest.Run -logFile -
```

```bash
$U -batchmode -projectPath $P -executeMethod Snapshot.Run -logFile -
```

```bash
$U -batchmode -nographics -quit -projectPath $P -executeMethod Builder.WebGL -logFile -
```

`SceneBuilder.Build` fixes the import settings, imports the TextMeshPro resources and writes `Assets/Scenes/Main.unity`. `SelfTest.Run` drives each case with scripted inputs and checks the rules and scoring (exit code 0 = pass). `Snapshot.Run` plays a scripted run and saves screenshots to `shots/`. `Builder.WebGL` writes the web build to `play/`.

| Folder | What it is |
|---|---|
| `UpperGIBleed/Assets/Scripts/` | The game: `Anatomy`, `CaseDefs`, `GameConfig` (tuning), `BloodSim`, `Tract`, `Procedure` (rules), `ProcedureView` (3D), `HUD`, `MenuUI`, `GameFlow` (runs and scoring), `Scoring`, `Profile`, `Leaderboard`, `GameSettings`, `GameAudio` (synthesised sounds), `UIKit`. |
| `UpperGIBleed/Assets/Editor/` | `SceneBuilder`, `SelfTest`, `Snapshot`, `Builder`. |
| `UpperGIBleed/Assets/Resources/Art/` | Blender output: `tract.fbx`, `tract_albedo.png`, `props.fbx`, `avatars/`. |
| `art/blender/tract.py` | The Blender script. |
| `play/` | The WebGL build served by GitHub Pages. |
| `web/` | The original canvas prototype and its tests (`web/tests/tests.html`). |

## The web prototype (`web/`)

### Run it locally

```bash
cd web && python3 -m http.server 8791
```

Then open http://localhost:8791/ (the game) or http://localhost:8791/tests/tests.html (the automated tests). `web/index.html` also opens straight from disk.

### Controls

| Key | Action |
|---|---|
| ↑ / ↓ | Move the scope forward / back (hold for continuous movement) |
| ← / → | Rotate the instrument tip round the wall (clockwise / anticlockwise, like a clock hand). It can slide all the way round, including the top. Hold for continuous movement |
| Tab | Next attachment (Wash → Cautery → Banding → Wash), once per keypress. Shift+Tab goes back |
| Space | Use the attachment: hold for Wash and Cautery, tap for Banding |
| P / Enter / R | Pause / sign off / retry from the debrief |

The camera stays steady. ↑/↓ slide the tissue toward or away from the tip, and ←/→ rotate the tip round the wall, so putting a lesion under the tip takes both. The tip rides a fixed circle on screen, so it follows each region's wall (shallow in the narrow oesophagus, deeper in the wide stomach). When you turn the tip it twists first and the shaft follows a moment later. On a phone the same controls appear as an on-screen pad (press-and-hold supported).

### The cases

| # | Case | Lesion | Treatment |
|---|---|---|---|
| 1 | Oesophageal variceal bleed | Ruptured varix, distal oesophagus | **Banding** (6 bands on the cap; a band fired away from the varix is wasted). Cautery is the wrong tool and says why |
| 2 | Gastric ulcer bleed | Lesser curve, just before the incisura angularis | **Cautery**. Banding cannot capture a flat gastric ulcer |
| 3 | Duodenal bulb ulcer bleed | Posterior wall of D1 | **Cautery**. Banding cannot capture a flat duodenal ulcer |

Pick a case on the start screen (keys 1 / 2 / 3) or from the debrief (N for the next case).

### The anatomy

One continuous tract: oesophagus (narrow, collapsed star-shaped lumen, pale squamous wall, blue varices) → gastro-oesophageal junction with a jagged Z-line → stomach (wide, red rugal mucosa, incisura angularis fold) → antrum → pylorus (rosette) → duodenal bulb (smooth, pale). You can withdraw all the way up to 20 cm, but **the scope cannot be advanced past the first part of the duodenum (D1)**. The view shows the current region and distance from the incisors, and landmarks are named as you cross them. Anatomy is defined in `config/anatomy.js`.

### How it plays

Steer and advance to the lesion by following the blood, wash to confirm the source, treat it with the right attachment (hold contact for cautery; tap to fire a band), then wash and re-inspect the site before signing off. Blood loss runs in real time; at 1500 mL the patient decompensates. The debrief and replay adapt to the case and to the attachments you used.

### Files

| File | What it is |
|---|---|
| `web/index.html` | Page layout and the on-screen controls. |
| `web/config/config.js` | **Tuning values**: scope speed, cautery contact time and radius, bands loaded, bleed rate, loss limit, trail length. |
| `web/config/anatomy.js` | **The anatomy**: lumen radius, regions, landmarks, wall colours, folds, and the D1 limit. |
| `web/cases/*.js` | **The cases**: start position, lesion type and position, which attachments suit it, cue and feedback text. |
| `web/js/game.js` | Scope rendering, blood simulation, attachments, input, debrief and replay. |
| `web/css/style.css` | Styles. |
| `web/tests/tests.html` | End-to-end tests that drive the real page with arrow, Space, Enter, P and R key events. |

### Adding a case

Copy a file in `cases/`, change the start, lesion (`kind`: `varix` or `ulcer`, position, radius), `suitable` flags, `flowDir` and the cue/feedback text, and add a `<script>` tag for it in `index.html` and `tests/tests.html`. `deg` can be anywhere round the wall (0 = 3 o'clock, 90 = bottom, 270 = top); `start.tip` sets where the tip begins.
