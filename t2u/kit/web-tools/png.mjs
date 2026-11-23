// A tiny PNG reader/writer (8-bit RGB / RGBA, not interlaced: what Chrome's screenshots are) — so the export tools can
// look at and cut pictures pixel for pixel without any extra package.
import zlib from 'node:zlib';

const SIG = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);

/** @returns {{ width:number, height:number, data:Uint8Array }} RGBA, straight alpha, rows top to bottom */
export function decodePng(buf) {
  if (!buf.subarray(0, 8).equals(SIG)) throw new Error('not a PNG');
  let p = 8;
  let width = 0;
  let height = 0;
  let type = 6;
  const idat = [];
  while (p < buf.length) {
    const len = buf.readUInt32BE(p);
    const name = buf.toString('ascii', p + 4, p + 8);
    const body = buf.subarray(p + 8, p + 8 + len);
    if (name === 'IHDR') {
      width = body.readUInt32BE(0);
      height = body.readUInt32BE(4);
      type = body[9];
      if (body[8] !== 8 || (type !== 6 && type !== 2) || body[12] !== 0) throw new Error(`unsupported PNG (depth ${body[8]}, type ${type}, interlace ${body[12]})`);
    } else if (name === 'IDAT') idat.push(body);
    else if (name === 'IEND') break;
    p += 12 + len;
  }
  const bpp = type === 6 ? 4 : 3;
  const raw = zlib.inflateSync(Buffer.concat(idat));
  const stride = width * bpp;
  const px = new Uint8Array(height * stride);
  for (let y = 0; y < height; y++) {
    const f = raw[y * (stride + 1)];
    const src = y * (stride + 1) + 1;
    const dst = y * stride;
    for (let x = 0; x < stride; x++) {
      const a = x >= bpp ? px[dst + x - bpp] : 0;
      const b = y > 0 ? px[dst + x - stride] : 0;
      const c = x >= bpp && y > 0 ? px[dst + x - stride - bpp] : 0;
      let v = raw[src + x];
      if (f === 1) v += a;
      else if (f === 2) v += b;
      else if (f === 3) v += (a + b) >> 1;
      else if (f === 4) {
        const pa = Math.abs(b - c);
        const pb = Math.abs(a - c);
        const pc = Math.abs(a + b - 2 * c);
        v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
      }
      px[dst + x] = v & 255;
    }
  }
  if (bpp === 4) return { width, height, data: px };
  const data = new Uint8Array(width * height * 4);
  for (let i = 0, j = 0; i < px.length; i += 3, j += 4) {
    data[j] = px[i];
    data[j + 1] = px[i + 1];
    data[j + 2] = px[i + 2];
    data[j + 3] = 255;
  }
  return { width, height, data };
}

const CRC = new Uint32Array(256).map((_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c >>> 0;
});
function chunk(name, body) {
  const head = Buffer.alloc(8);
  head.writeUInt32BE(body.length, 0);
  head.write(name, 4, 'ascii');
  let c = 0xffffffff;
  for (let i = 4; i < 8; i++) c = CRC[(c ^ head[i]) & 255] ^ (c >>> 8);
  for (let i = 0; i < body.length; i++) c = CRC[(c ^ body[i]) & 255] ^ (c >>> 8);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE((c ^ 0xffffffff) >>> 0, 0);
  return Buffer.concat([head, body, crc]);
}

/** RGBA (straight alpha) → PNG. The same pixels always give the same bytes. */
export function encodePng({ width, height, data }) {
  const stride = width * 4;
  const raw = Buffer.alloc(height * (stride + 1));
  for (let y = 0; y < height; y++) Buffer.from(data.buffer, data.byteOffset + y * stride, stride).copy(raw, y * (stride + 1) + 1);
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8;
  ihdr[9] = 6;
  return Buffer.concat([SIG, chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}

/** a w×h piece of a picture */
export function crop(img, x, y, w, h) {
  const data = new Uint8Array(w * h * 4);
  for (let r = 0; r < h; r++) data.set(img.data.subarray(((y + r) * img.width + x) * 4, ((y + r) * img.width + x + w) * 4), r * w * 4);
  return { width: w, height: h, data };
}

/** how two pictures of the same size differ: pixels that differ at all, the largest channel difference, and the box */
export function diffPng(a, b) {
  if (a.width !== b.width || a.height !== b.height) return { size: true, pixels: -1, max: 255 };
  let pixels = 0;
  let max = 0;
  let x0 = a.width, y0 = a.height, x1 = -1, y1 = -1;
  for (let i = 0; i < a.data.length; i += 4) {
    const d = Math.max(Math.abs(a.data[i] - b.data[i]), Math.abs(a.data[i + 1] - b.data[i + 1]), Math.abs(a.data[i + 2] - b.data[i + 2]), Math.abs(a.data[i + 3] - b.data[i + 3]));
    if (!d) continue;
    pixels++;
    if (d > max) max = d;
    const x = (i >> 2) % a.width;
    const y = Math.floor((i >> 2) / a.width);
    if (x < x0) x0 = x;
    if (x > x1) x1 = x;
    if (y < y0) y0 = y;
    if (y > y1) y1 = y;
  }
  return { pixels, max, box: pixels ? [x0, y0, x1, y1] : null };
}

// ---- comparing two pictures of the same thing drawn by two renderers (the web and the importer's rules; the web and Unity)
// Two renderers never agree on the last half pixel of an edge (Chrome itself draws the text of a scrolling list a
// shade differently from text outside one). So:
//  - a pixel only counts as different when the other picture has no pixel of that colour (give or take TOL per
//    channel) within SLIP device pixels of it;
//  - and a picture only FAILS when such pixels form an area (every pixel of a SOLID x SOLID square): a missing
//    element, a wrong colour, a box off by two design pixels is an area; the fringe of an edge is a hairline.
export const COMPARE = { TOL: 24, SLIP: 3, SOLID: 5 };

/** @param scale device pixels per design unit (SLIP = one and a half design units)
 *  @returns {{ n: number, solid: number, mask: Uint8Array }} pixels that really differ, those that form an area, where */
export function unlikePng(a, b, scale = 2) {
  const { TOL, SOLID } = COMPARE;
  const SLIP = Math.round(scale * 1.5);
  const w = a.width, h = a.height;
  const mask = new Uint8Array(w * h);
  let n = 0;
  const near = (p, q, i, x, y) => {
    // is there a pixel like p[i] around (x, y) in q?
    for (let dy = -SLIP; dy <= SLIP; dy++) {
      const yy = y + dy;
      if (yy < 0 || yy >= h) continue;
      for (let dx = -SLIP; dx <= SLIP; dx++) {
        const xx = x + dx;
        if (xx < 0 || xx >= w) continue;
        const j = (yy * w + xx) * 4;
        if (Math.abs(p.data[i] - q.data[j]) <= TOL && Math.abs(p.data[i + 1] - q.data[j + 1]) <= TOL && Math.abs(p.data[i + 2] - q.data[j + 2]) <= TOL) return true;
      }
    }
    return false;
  };
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const i = (y * w + x) * 4;
      if (Math.abs(a.data[i] - b.data[i]) <= TOL && Math.abs(a.data[i + 1] - b.data[i + 1]) <= TOL && Math.abs(a.data[i + 2] - b.data[i + 2]) <= TOL) continue;
      if (near(a, b, i, x, y) && near(b, a, i, x, y)) continue;
      mask[y * w + x] = 1;
      n++;
    }
  }
  // areas: pixels whose whole SOLID x SOLID neighbourhood differs (a summed-area table makes it one pass)
  let solid = 0;
  if (n >= SOLID * SOLID) {
    const sat = new Int32Array((w + 1) * (h + 1));
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) sat[(y + 1) * (w + 1) + x + 1] = mask[y * w + x] + sat[y * (w + 1) + x + 1] + sat[(y + 1) * (w + 1) + x] - sat[y * (w + 1) + x];
    for (let y = 0; y + SOLID <= h; y++) for (let x = 0; x + SOLID <= w; x++) if (sat[(y + SOLID) * (w + 1) + x + SOLID] - sat[y * (w + 1) + x + SOLID] - sat[(y + SOLID) * (w + 1) + x] + sat[y * (w + 1) + x] === SOLID * SOLID) solid++;
  }
  return { n, solid, mask };
}

/** one | the other | where they differ (white: really different; grey: within the tolerance) */
export function sideBySide(a, b, mask) {
  const w = a.width, h = a.height;
  const out = { width: w * 3, height: h, data: new Uint8Array(w * 3 * h * 4) };
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const i = (y * w + x) * 4;
      const d = Math.max(Math.abs(a.data[i] - b.data[i]), Math.abs(a.data[i + 1] - b.data[i + 1]), Math.abs(a.data[i + 2] - b.data[i + 2]));
      for (const [k, src, v] of [[0, a, null], [1, b, null], [2, null, mask[y * w + x] ? 255 : d > 0 ? 70 : 0]]) {
        const o = (y * w * 3 + k * w + x) * 4;
        out.data[o] = src ? src.data[i] : v;
        out.data[o + 1] = src ? src.data[i + 1] : v;
        out.data[o + 2] = src ? src.data[i + 2] : v;
        out.data[o + 3] = 255;
      }
    }
  }
  return out;
}
