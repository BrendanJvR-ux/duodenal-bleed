/* Case 3: bleeding ulcer in the duodenal bulb (D1). Cautery is the intended treatment; banding cannot capture an ulcer. */
window.CASES = window.CASES || [];
window.CASES.push({
  id: 'duodenal', short: 'Duodenum', title: 'Duodenal bulb ulcer bleed',
  where: 'Posterior wall of the duodenal bulb (D1)',
  vignette: 'Fictional patient with epigastric pain and melaena. The scope has just passed the pylorus into the duodenal bulb, and fresh blood is pooling ahead.',
  start: { z: 18.75, tip: 60 },
  lesion: { kind: 'ulcer', style: 'duodenal', deg: 150, w: 21.2, radius: 0.2 },
  flowDir: -1,                // blood pools in the bulb and refluxes back toward the pylorus
  bleedMl: 16, emit: 0.95,
  intended: 'cautery', sourceName: 'ulcer',
  suitable: { wash: true, cautery: true, banding: false },
  cues: {
    reveal: 'Fresh blood wells up from the crater: this is the source.',
    banding: 'Banding: the ulcer is flat and fibrotic, so it cannot be captured in the cap, and the duodenal wall is thin. Try another attachment.',
    bandingMiss: 'Banding: nothing in the cap, so there is no lesion to capture here.',
  },
  notes: {
    wash: 'Fresh blood welling back from the crater showed it was still actively bleeding.',
    cautery: 'Thermal contact on the crater coagulates the exposed vessel.',
    banding: 'A flat, fibrotic duodenal ulcer cannot be sucked into the cap, and the thin duodenal wall makes banding unsafe. Banding is for varices.',
  },
});
