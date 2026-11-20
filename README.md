# Upper GI Bleed

A training prototype: find and treat a fictional upper GI bleed with the arrow keys, Tab and Space. Three cases share one anatomically ordered tract.

**Play it:** https://brendanjvr-ux.github.io/duodenal-bleed/

## Run it locally

```bash
python3 -m http.server 8791
```

Then open http://localhost:8791/ (the game) or http://localhost:8791/tests/tests.html (the automated tests). `index.html` also opens straight from disk.

## Controls

| Key | Action |
|---|---|
| ↑ / ↓ | Move the scope forward / back (hold for continuous movement) |
| ← / → | Steer the scope left / right (hold for continuous movement) |
| Tab | Next attachment (Wash → Cautery → Banding → Wash), once per keypress. Shift+Tab goes back |
| Space | Use the attachment: hold for Wash and Cautery, tap for Banding |
| P / Enter / R | Pause / sign off / retry from the debrief |

The instrument tip stays put on screen. ↑/↓ slide the tissue toward or away from it and ←/→ swing the view sideways, so putting the ulcer under the tip takes both. On a phone the same controls appear as an on-screen pad (press-and-hold supported).

## The cases

| # | Case | Lesion | Treatment |
|---|---|---|---|
| 1 | Oesophageal variceal bleed | Ruptured varix, distal oesophagus | **Banding** (6 bands on the cap; a band fired away from the varix is wasted). Cautery is the wrong tool and says why |
| 2 | Gastric ulcer bleed | Lesser curve, just before the incisura angularis | **Cautery**. Banding cannot capture a flat gastric ulcer |
| 3 | Duodenal bulb ulcer bleed | Posterior wall of D1 | **Cautery**. Banding cannot capture a flat duodenal ulcer |

Pick a case on the start screen (keys 1 / 2 / 3) or from the debrief (N for the next case).

## The anatomy

One continuous tract: oesophagus (narrow, collapsed star-shaped lumen, pale squamous wall, blue varices) → gastro-oesophageal junction with a jagged Z-line → stomach (wide, red rugal mucosa, incisura angularis fold) → antrum → pylorus (rosette) → duodenal bulb (smooth, pale). You can withdraw all the way up to 20 cm, but **the scope cannot be advanced past the first part of the duodenum (D1)**. The view shows the current region and distance from the incisors, and landmarks are named as you cross them. Anatomy is defined in `config/anatomy.js`.

## How it plays

Steer and advance to the lesion by following the blood, wash to confirm the source, treat it with the right attachment (hold contact for cautery; tap to fire a band), then wash and re-inspect the site before signing off. Blood loss runs in real time; at 1500 mL the patient decompensates. The debrief and replay adapt to the case and to the attachments you used.

## Files

| File | What it is |
|---|---|
| `index.html` | Page layout and the on-screen controls. |
| `config/config.js` | **Tuning values**: scope speed, cautery contact time and radius, bands loaded, bleed rate, loss limit, trail length. |
| `config/anatomy.js` | **The anatomy**: lumen radius, regions, landmarks, wall colours, folds, and the D1 limit. |
| `cases/*.js` | **The cases**: start position, lesion type and position, which attachments suit it, cue and feedback text. |
| `js/game.js` | Scope rendering, blood simulation, attachments, input, debrief and replay. |
| `css/style.css` | Styles. |
| `tests/tests.html` | End-to-end tests that drive the real page with arrow, Space, Enter, P and R key events. |

## Adding a case

Copy a file in `cases/`, change the start, lesion (`kind`: `varix` or `ulcer`, position, radius), `suitable` flags, `flowDir` and the cue/feedback text, and add a `<script>` tag for it in `index.html` and `tests/tests.html`. Choose the lesion angle so it can be reached by steering within about ±0.7 (angles between roughly 25° and 115° work well).
