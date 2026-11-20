# Duodenal Bleed

A training prototype: control a fictional bleeding duodenal-bulb ulcer using only ↑ ↓ ← → and Space.

**Play it:** https://brendanjvr-ux.github.io/duodenal-bleed/

## Run it locally

```bash
python3 -m http.server 8791
```

Then open http://localhost:8791/ (the game) or http://localhost:8791/tests/tests.html (the automated tests). `index.html` also opens straight from disk.

## Controls

| Key | Action |
|---|---|
| ↑ / ↓ | Scope forward / back (hold for continuous movement) |
| ← / → | Previous / next attachment (Wash → Cautery → Banding → Wash), once per keypress |
| Space | Use the attachment: hold for Wash and Cautery, tap for Banding |
| P / Enter / R | Pause / sign off / retry from the debrief |

On a phone the same controls appear as an on-screen pad (press-and-hold supported).

## How it plays

The scope starts in the duodenal bulb with a fresh-blood trail ahead. Follow it to the ulcer, wash to confirm the source, switch to cautery and hold contact on the lesion until the bleeding stops, then wash and re-inspect the site before signing off. Banding cannot capture this flat, fibrotic ulcer, so it only produces a cue. Blood loss runs in real time; at 1500 mL the patient decompensates.

## Files

| File | What it is |
|---|---|
| `index.html` | Page layout and the on-screen controls. |
| `config/config.js` | **Tuning values**: scope speed, cautery contact time and radius, bleed rate, loss limit, trail length. |
| `cases/duodenal-ulcer.js` | **The case**: lesion position, which attachments suit it, banding cue text. |
| `js/game.js` | Scope rendering, blood simulation, attachments, input, debrief and replay. |
| `css/style.css` | Styles. |
| `tests/tests.html` | End-to-end tests that drive the real page with arrow, Space, Enter, P and R key events. |

## Adding a case

Copy `cases/duodenal-ulcer.js`, change the lesion and `suitable` flags (set `banding: true` for a lesion a band can capture; the deployment hook is reserved in `attemptBand()`), and load it in `index.html` instead.
