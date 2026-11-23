// Turns the photographs of UI elements (bot.mjs export) into sprites for Unity:
//   trim the empty margin → find the part that can stretch (9-slice) → keep one copy of pictures that are the same.
// Pure pixel work on what Chrome drew: no CSS is read here, so whatever the web shows is what Unity gets.
import crypto from 'node:crypto';
import { crop } from './png.mjs';

const KEEP = 2; // pixels of a stretchable run that become the stretched middle of the sprite
const GUARD = 1; // pixels of the run that go to each border
const MIN_RUN = 8; // a run shorter than this is not worth slicing...
const MIN_SHARE = 0.3; // ...and so is one that is a small part of the picture (a slow gradient has short runs: it
// stretches as a whole instead, which is also what a designer resizing it in Unity wants)

/** Chrome does not draw the same element to the very last bit twice: the dither of a gradient and the fringe of a
 *  rounded corner move by a step or two between runs. Two pictures are THE SAME when few pixels differ by more than
 *  the dither step and none by much — so a sprite keeps its name and its file from one export to the next, and git
 *  only sees the sprites that really changed. (Two looks that differ on purpose differ over their whole area.) */
export function samePicture(a, b) {
  if (a.width !== b.width || a.height !== b.height) return false;
  let off = 0;
  const limit = Math.max(8, a.width * a.height * 4 * 0.02);
  for (let i = 0; i < a.data.length; i++) {
    const d = Math.abs(a.data[i] - b.data[i]);
    if (d <= TOL) continue;
    if (d > 32 || ++off > limit) return false;
  }
  return true;
}

const FAINT = 3; // alpha up to this is nothing (the far end of a soft shadow flickers between 0 and 1)
/** box of the pixels that are drawn at all, or null */
export function inkBox(img) {
  const { width: w, height: h, data } = img;
  let x0 = w, y0 = h, x1 = -1, y1 = -1;
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      if (data[(y * w + x) * 4 + 3] <= FAINT) continue;
      if (x < x0) x0 = x;
      if (x > x1) x1 = x;
      if (y < y0) y0 = y;
      if (y > y1) y1 = y;
    }
  }
  return x1 < 0 ? null : { x: x0, y: y0, w: x1 - x0 + 1, h: y1 - y0 + 1 };
}

// Chrome dithers gradients: two columns of a top-to-bottom gradient are the same colour give or take one step.
// TOL is that step. A run is measured against its FIRST column, so a slow side-to-side gradient cannot creep in.
const TOL = 1;
const sameCol = (img, a, b) => {
  const { width: w, height: h, data } = img;
  for (let y = 0; y < h; y++) {
    const i = (y * w + a) * 4, j = (y * w + b) * 4;
    if (Math.abs(data[i] - data[j]) > TOL || Math.abs(data[i + 1] - data[j + 1]) > TOL || Math.abs(data[i + 2] - data[j + 2]) > TOL || Math.abs(data[i + 3] - data[j + 3]) > TOL) return false;
  }
  return true;
};
const sameRow = (img, a, b) => {
  const w4 = img.width * 4;
  for (let x = 0; x < w4; x++) if (Math.abs(img.data[a * w4 + x] - img.data[b * w4 + x]) > TOL) return false;
  return true;
};

/** the longest run of columns (or rows) that all match the run's first one: [first, last] or null */
function longestRun(n, same) {
  let best = null;
  let start = 0;
  for (let i = 1; i <= n; i++) {
    if (i < n && same(start, i)) continue;
    if (i - start >= Math.max(MIN_RUN, MIN_SHARE * n) && (!best || i - start > best[1] - best[0] + 1)) best = [start, i - 1];
    start = i;
  }
  return best;
}

/**
 * Cut the stretchable middle out of a picture: returns the smaller picture and its 9-slice border [L, B, R, T].
 * The border takes GUARD pixels of the run with it, so the seam between a border and the stretched middle lies
 * between two identical pixels: a renderer that blends neighbours there (Unity, CSS) blends a colour with itself.
 */
export function slice(img, on = true) {
  if (!on) return { img, border: [0, 0, 0, 0] };
  let out = img;
  let L = 0, R = 0, T = 0, B = 0;
  const cols = longestRun(img.width, (a, b) => sameCol(img, a, b));
  if (cols) {
    const [a, b] = cols;
    L = a + GUARD;
    R = img.width - 1 - b + GUARD;
    const w = L + KEEP + R;
    const data = new Uint8Array(w * out.height * 4);
    for (let y = 0; y < out.height; y++) {
      const row = out.data.subarray(y * out.width * 4, (y + 1) * out.width * 4);
      data.set(row.subarray(0, a * 4), y * w * 4); // the left part
      for (let k = 0; k < GUARD + KEEP + GUARD; k++) data.set(row.subarray(a * 4, a * 4 + 4), (y * w + a + k) * 4); // the run: its first column
      data.set(row.subarray((b + 1) * 4), (y * w + a + GUARD + KEEP + GUARD) * 4); // the right part
    }
    out = { width: w, height: out.height, data };
  }
  const rows = longestRun(out.height, (a, b) => sameRow(out, a, b));
  if (rows) {
    const [a, b] = rows;
    T = a + GUARD;
    B = out.height - 1 - b + GUARD;
    const h = T + KEEP + B;
    const w4 = out.width * 4;
    const data = new Uint8Array(h * w4);
    data.set(out.data.subarray(0, a * w4), 0);
    for (let k = 0; k < GUARD + KEEP + GUARD; k++) data.set(out.data.subarray(a * w4, (a + 1) * w4), (a + k) * w4);
    data.set(out.data.subarray((b + 1) * w4), (a + GUARD + KEEP + GUARD) * w4);
    out = { width: out.width, height: h, data };
  }
  return { img: out, border: [L, B, R, T] };
}

/**
 * Collects the sprites of one export. add(photo, clip, box, hint) → { skin, skinBox } or null when nothing was drawn.
 * @param scale device pixels per CSS pixel of the photographs
 * @param previous the sprites of the export before this one: { name: { border, img } } — a picture that is still the
 *   same keeps that name and that very image
 */
export function createSpriteSet(scale, previous = {}) {
  const byHash = new Map();
  const names = new Set();
  const sprites = {};
  const warnings = [];
  return {
    sprites,
    warnings,
    /** @param photo decoded PNG of `clip` (CSS px, already inside the page); @param box the element's own box (CSS px);
     *  @param u CSS px per design unit; @param edges which sides of the clip were cut by the page edge;
     *  @param stretch false for a picture that must never be 9-sliced (an icon) */
    add(photo, clip, box, u, hint, edges, stretch = true) {
      const ink = inkBox(photo);
      if (!ink) return null;
      if ((ink.x === 0 && !edges.left) || (ink.y === 0 && !edges.top) || (ink.x + ink.w === photo.width && !edges.right) || (ink.y + ink.h === photo.height && !edges.bottom))
        warnings.push(`${hint}: its picture reaches the edge of the photograph (a shadow wider than the margin is cut)`);
      const cut = crop(photo, ink.x, ink.y, ink.w, ink.h);
      const { img, border } = slice(cut, stretch);
      const hash = crypto.createHash('sha256').update(`${img.width}x${img.height}:${border.join(',')}:`).update(img.data).digest('hex');
      let name = byHash.get(hash);
      if (!name) {
        // the same picture as one already taken in this export...
        for (const [n, sp] of Object.entries(sprites)) {
          if (sp.border.join() !== border.join() || !samePicture(sp.img, img)) continue;
          name = n;
          break;
        }
      }
      const base = `ui_${hint}`.replace(/_+/g, '_').replace(/_$/, '').toLowerCase();
      if (!name) {
        // ...or the same as the last export had under this name (or this name with a number): keep name and image
        for (const [n, sp] of Object.entries(previous)) {
          if (names.has(n) || !(n === base || /^_\d+$/.test(n.slice(base.length)) && n.startsWith(base))) continue;
          if (sp.border.join() !== border.join() || !samePicture(sp.img, img)) continue;
          name = n;
          names.add(n);
          sprites[n] = { w: sp.img.width, h: sp.img.height, border, img: sp.img };
          break;
        }
      }
      if (!name) {
        name = base;
        for (let k = 2; names.has(name) || previous[name]; k++) name = `${base}_${k}`;
        names.add(name);
        sprites[name] = { w: img.width, h: img.height, border, img };
      }
      byHash.set(hash, name);
      // how far the picture reaches outside the element's box, in design units: left, top, right, bottom
      const l = clip.x + ink.x / scale, t = clip.y + ink.y / scale;
      const r2 = (v) => Math.round((v / u) * 100) / 100 || 0;
      const skinBox = [r2(box.x - l), r2(box.y - t), r2(l + ink.w / scale - (box.x + box.width)), r2(t + ink.h / scale - (box.y + box.height))];
      return { skin: name, skinBox };
    },
  };
}
