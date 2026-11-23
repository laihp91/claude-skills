// Sound service. Logic only says out('sfx', {n, rate}); this file decides how it sounds.
// Unity: AudioService with the same sound names. Put real files in public/assets/audio/<name>.ogg|mp3 and
// list them in public/assets/manifest.json; until then a short beep stands in.
export function createAudio() {
  let ctx = null;
  const tones = { pop: 660, bomb: 110, tap_miss: 220, win: 880, lose: 160, click: 520 };
  return {
    unlock() {
      ctx ??= new (window.AudioContext || window.webkitAudioContext)();
      if (ctx.state === 'suspended') ctx.resume();
    },
    play(name, { rate = 1, vol = 0.25 } = {}) {
      if (!ctx) return;
      const o = ctx.createOscillator();
      const g = ctx.createGain();
      o.frequency.value = (tones[name] ?? 440) * rate;
      g.gain.setValueAtTime(vol, ctx.currentTime);
      g.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + 0.15);
      o.connect(g).connect(ctx.destination);
      o.start();
      o.stop(ctx.currentTime + 0.16);
    },
  };
}
