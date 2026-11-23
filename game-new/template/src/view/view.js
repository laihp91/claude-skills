// Rendering (three.js). Reads the game state every frame; never changes it. May use Math.random for visuals.
// Unity: becomes View MonoBehaviours (sprites/meshes) that read the C# GameModel the same way.
import * as THREE from 'three';

/** @param now game-time clock in seconds (effects age with GAME time, so replays and Unity look the same) */
export function createView(canvas, config, now) {
  const W = config.world.w;
  const H = config.world.h;
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, preserveDrawingBuffer: true });
  renderer.setPixelRatio(Math.min(2, window.devicePixelRatio || 1));
  const scene = new THREE.Scene();
  scene.background = new THREE.Color('#bfe6ff');
  // world: x in [-W/2, W/2], y in [0, H] (y up) — the same numbers the logic uses
  const camera = new THREE.OrthographicCamera(-W / 2, W / 2, H, 0, -10, 10);
  const geo = new THREE.CircleGeometry(1, 48);
  const ringGeo = new THREE.RingGeometry(0.85, 1, 48);
  const colors = { normal: '#3d8bff', bomb: '#2b2f3a' };
  const meshes = new Map();
  const fx = [];

  function meshFor(b) {
    let m = meshes.get(b.id);
    if (!m) {
      m = new THREE.Mesh(geo, new THREE.MeshBasicMaterial({ color: colors[b.kind], transparent: true, opacity: 0.92 }));
      scene.add(m);
      meshes.set(b.id, m);
    }
    return m;
  }

  /** visual-only effect for a game event (Math.random allowed here) */
  function onEvent(name, d) {
    if (name !== 'pop') return;
    const m = new THREE.Mesh(ringGeo, new THREE.MeshBasicMaterial({ color: d.kind === 'bomb' ? '#ff5a3c' : '#ffffff', transparent: true }));
    m.position.set(d.x, d.y, 1);
    m.rotation.z = Math.random() * Math.PI;
    scene.add(m);
    fx.push({ m, born: now() });
  }

  function render(state) {
    const alive = new Set();
    for (const b of state.bubbles) {
      alive.add(b.id);
      const m = meshFor(b);
      m.position.set(b.x, b.y, 0);
      m.scale.setScalar(b.r * b.s);
      m.material.opacity = b.popping ? Math.max(0, 0.92 * (1 - (b.s - 1) / 0.4)) : 0.92;
    }
    for (const [id, m] of meshes) if (!alive.has(id)) (scene.remove(m), m.material.dispose(), meshes.delete(id));
    for (let i = fx.length - 1; i >= 0; i--) {
      const f = fx[i];
      const k = Math.min(1, (now() - f.born) / 0.3);
      f.m.scale.setScalar(0.6 + 1.2 * k);
      f.m.material.opacity = 1 - k;
      if (k >= 1) (scene.remove(f.m), f.m.material.dispose(), fx.splice(i, 1));
    }
    renderer.render(scene, camera);
  }

  /** canvas pixel size follows its CSS size */
  function resize(w, h) {
    renderer.setSize(w, h, false);
  }

  /** screen point -> world point (the logic only ever sees world coordinates) */
  function toWorld(clientX, clientY) {
    const r = canvas.getBoundingClientRect();
    return { x: ((clientX - r.left) / r.width - 0.5) * W, y: (1 - (clientY - r.top) / r.height) * H };
  }

  return { render, resize, toWorld, onEvent, renderer, scene, camera };
}
