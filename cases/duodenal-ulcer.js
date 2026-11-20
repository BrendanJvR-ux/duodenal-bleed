/* One case = one lesion + which attachments suit it. A future case can set suitable.banding to true. */
window.CASE = {
  lesion: { deg: 50, w: 3.0, radius: 0.2 },
  suitable: { wash: true, cautery: true, banding: false },
  bandingCue: 'Banding: the ulcer is flat and fibrotic, so it cannot be captured in the cap. Try another attachment.',
  bandingMissCue: 'Banding: nothing in the cap, so there is no lesion to capture here.',
};
