/* The upper GI tract as one continuous tube, measured along its length (w, in scope units).
   Oesophagus -> gastro-oesophageal junction -> stomach -> pylorus -> first part of the duodenum (D1).
   Edit this file to change the anatomy; every case shares it. */
window.ANATOMY = {
  end: 22.8,            // end of D1, the superior duodenal flexure, where the lumen turns away
  scopeMax: 21.7,       // the scope cannot be advanced beyond the first part of the duodenum
  zline: 7.0,           // squamocolumnar junction = gastro-oesophageal junction

  // lumen radius along the tract: narrow oesophagus, wide stomach, narrowing antrum, pyloric canal, bulb
  radius: [[0, 0.58], [5.5, 0.60], [6.7, 0.68], [7.15, 1.25], [8.3, 1.75], [13.5, 1.8], [16.5, 1.3], [17.9, 1.0], [18.4, 0.74], [18.9, 0.96], [19.6, 0.98], [22.8, 0.76]],

  // sideways drift of the lumen axis: straight oesophagus and bulb, the antrum curves toward the pylorus, D1 turns at its end
  pathX: [[0, 0], [14.5, 0], [16.2, 0.35], [17.5, 0.55], [18.4, 0.1], [19.0, 0], [21.5, 0], [22.8, 1.3]],
  pathY: [[0, 0], [14.5, 0], [16.2, -0.25], [17.5, -0.15], [18.4, 0], [22.0, 0], [22.8, 0.25]],

  regions: [
    { id: 'oes',    name: 'Oesophagus',          to: 7.0 },
    { id: 'cardia', name: 'Gastric cardia',      to: 8.8 },
    { id: 'body',   name: 'Gastric body',        to: 14.6 },
    { id: 'antrum', name: 'Gastric antrum',      to: 18.4 },
    { id: 'd1',     name: 'Duodenal bulb (D1)',  to: 22.9 },
  ],

  cm: [[0, 20], [7.0, 40], [18.4, 66], [22.8, 72]],        // approximate distance from the incisors

  landmarks: [
    { w: 7.0,  name: 'Gastro-oesophageal junction (Z-line)' },
    { w: 14.9, name: 'Incisura angularis' },
    { w: 18.4, name: 'Pylorus' },
  ],

  // wall colour by region: pale squamous oesophagus, red-orange gastric mucosa (paler in the antrum), pink-cream duodenum
  palette: [
    { to: 7.0,  c: [214, 160, 152], sharp: true },          // sharp, jagged change at the Z-line
    { to: 14.6, c: [206, 92, 74] },
    { to: 18.2, c: [218, 114, 94] },
    { to: 22.9, c: [226, 152, 120] },
  ],

  // folds: {w, a0 (deg), sweep (deg), prot (how far it reaches into the lumen)}; D1 is smooth, the pylorus is a full rosette
  folds: [
    { w: 7.35, a0: 95,  sweep: 150, prot: 0.20 },            // flap valve at the cardia
    { w: 9.6,  a0: 200, sweep: 130, prot: 0.14 },
    { w: 11.6, a0: -40, sweep: 120, prot: 0.14 },
    { w: 14.9, a0: -85, sweep: 135, prot: 0.50 },            // incisura angularis (lesser curve)
    { w: 18.4, a0: 0,   sweep: 359.5, prot: 0.58, uniform: true, rosette: true },   // pylorus
  ],
};
