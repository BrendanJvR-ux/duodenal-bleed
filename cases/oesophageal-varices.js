/* Case 1: oesophageal variceal bleed. Banding is the intended treatment; cautery is not suitable. */
window.CASES = window.CASES || [];
window.CASES.push({
  id: 'varices', short: 'Oesophagus', title: 'Oesophageal variceal bleed',
  where: 'Distal oesophagus, about 4 cm above the gastro-oesophageal junction',
  vignette: 'Fictional patient with cirrhosis and portal hypertension, vomiting fresh blood. Varices run down the distal oesophagus and one has ruptured. A band-ligation cap is loaded on the scope.',
  start: { z: 1.6 },
  lesion: { kind: 'varix', deg: 115, w: 5.9, radius: 0.13 },        // deg: position round the wall (0 = 3 o'clock, 90 = bottom)
  varices: {                                                       // longitudinal blue cords; `bleed` is the cord that has ruptured
    w0: 4.3, w1: 7.0, bleed: 1,
    cords: [{ deg: 30, ph: 0.5 }, { deg: 115, ph: 2.0 }, { deg: 165, ph: 4.1 }, { deg: 230, ph: 1.3 }, { deg: 290, ph: 3.3 }, { deg: 350, ph: 5.2 }],
  },
  flowDir: 1,                 // blood runs down the oesophagus toward the stomach
  bleedMl: 22, emit: 1.3,     // brisk
  intended: 'banding', sourceName: 'varix',
  suitable: { wash: true, cautery: false, banding: true },
  cues: {
    reveal: 'Blood spurts from a ruptured varix: this is the source.',
    cautery: 'Cautery: not for varices. The wall is paper thin and thermal injury can make it bleed more. Band it.',
    bandingMiss: 'Band fired with no varix in the cap: that band is wasted and leaves a nodule of normal mucosa.',
  },
  notes: {
    wash: 'Fresh blood spurting from the ruptured varix confirmed an active bleed.',
    cautery: 'Cautery does not belong on a varix: the wall is thin, so thermal injury can deepen the bleed or ulcerate. Banding strangulates the vessel.',
    banding: 'Banding captures the varix in the cap and strangulates it, which is the standard endoscopic treatment for a bleeding oesophageal varix.',
  },
});
