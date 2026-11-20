/* Case 2: bleeding gastric ulcer on the lesser curve. Cautery is the intended treatment; banding cannot capture an ulcer. */
window.CASES = window.CASES || [];
window.CASES.push({
  id: 'gastric', short: 'Stomach', title: 'Gastric ulcer bleed',
  where: 'Lesser curve of the stomach, just before the incisura angularis',
  vignette: 'Fictional patient on regular anti-inflammatories with black stools and dizziness. The scope is in the gastric body and a pool of blood has collected in the dependent fundus.',
  start: { z: 9.4 },
  lesion: { kind: 'ulcer', style: 'gastric', deg: 28, w: 14.0, radius: 0.28 },
  flowDir: -1,                // blood runs back along the lesser curve and pools in the dependent fundus
  bleedMl: 14, emit: 0.8,
  intended: 'cautery', sourceName: 'ulcer',
  suitable: { wash: true, cautery: true, banding: false },
  cues: {
    reveal: 'Fresh blood wells up from the ulcer base: this is the source.',
    banding: 'Banding: a gastric ulcer is a flat crater in a thick wall. There is nothing to suck into the cap, and a band here risks perforation. Try another attachment.',
    bandingMiss: 'Banding: nothing in the cap, so there is no lesion to capture here.',
  },
  notes: {
    wash: 'Fresh blood welling from the ulcer base showed it was still actively bleeding.',
    cautery: 'Thermal contact on the ulcer base coagulates the exposed vessel.',
    banding: 'A flat, fibrotic gastric ulcer cannot be sucked into the cap, so banding cannot work and risks perforation. Banding is for varices.',
  },
});
