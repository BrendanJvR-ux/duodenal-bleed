# Duodenal Bleed

A single-file training prototype: control a fictional bleeding duodenal-bulb ulcer using only ↑ ↓ ← → and Space.

Open `duodenal-bleed.html` in a browser, or serve it:

```bash
python3 -m http.server 8791
```

| Key | Action |
|---|---|
| ↑ / ↓ | Scope forward / back (hold for continuous) |
| ← / → | Previous / next attachment (Wash → Cautery → Banding) |
| Space | Use the attachment (hold for Wash and Cautery, tap for Banding) |
| P / Enter | Pause / sign off |

Tuning values are in `CONFIG` at the top of the HTML file.
