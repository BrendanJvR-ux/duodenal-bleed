/* Tuning values for the case. Edit freely; the game reads these at load. */
window.CONFIG = {
  scopeSpeed: 0.62,          // world units / second while ↑ or ↓ is held
  scopeAccel: 3.2,           // easing so taps move a small, controllable amount
  steerSpeed: 0.55,          // steering (←/→) in units / second; 1 unit = 230 px of sideways shift
  steerAccel: 4.0,
  steerMax: 0.75,            // how far the scope can angle either way
  cauteryContactSec: 1.6,    // continuous contact needed to coagulate (the "contact interval")
  cauteryContactRadius: 26,  // px: probe tip must be this close to the lesion centre
  cauteryDecay: 1.5,         // progress lost per second when contact is broken
  washRadius: 56,            // px: water-jet footprint
  washClearRate: 5.0,        // blood cleared per second at the jet centre
  bandRadius: 36,            // px: banding cap
  bandsLoaded: 6,            // bands on the ligation cap (varix case)
  bandCooldown: 0.9,         // s between banding attempts
  bleedMlPerSec: 16,         // real-time blood loss while the lesion bleeds
  lossLimitMl: 1500,         // patient decompensates here
  clearViewSecToSignOff: 3,  // how long the site must be seen clear after control
  clearCover: 0.18,          // blood thickness over the lesion that counts as "clear"
  emitPerSec: 0.95,          // blood welling into the crater
  flowBase: 0.2,             // blood thickness before it starts to run along the wall
  flowPerCell: 0.04,         // extra thickness needed per cell of distance from the source (limits trail length)
  trailWarmupTicks: 120,     // 0.1 s ticks pre-simulated so a trail exists on the first frame
};
