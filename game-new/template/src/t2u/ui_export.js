// Measures the DOM UI and describes it as data, so the Unity kit (T2U Kit ▸ Import UI Layouts) can rebuild every
// screen as a uGUI prefab that looks the same. Units: design pixels (frame width = 1080). Tooling: not ported.
//
// What is described (ui.json, version 2):
//   - every [data-ui] element of every [data-ui-screen]: its box, whether it starts hidden, its text and font
//     (size, colour, shadow, spacing, wrapping, where it sits in its box), its image, its button id;
//   - its LOOK: whatever CSS paints for it (gradient, border, rounded corners, shadows, ::before/::after, a filtered
//     image) is photographed by tools/t2u/bot.mjs into a PNG — `skin` (a sprite, 9-sliced where it can stretch) and
//     `skinBox` (how far the picture reaches outside the box). A plain rectangle of one colour stays a colour (`bg`);
//   - STATES: for every class the code switches at runtime (data-ui-states on the UI root, ui.js cls()), what changes
//     on the element and below it (another skin, colour, place, animation...), for every combination that can occur;
//   - bars the code fills (data-ui-fill), lists built from config (data-ui-list) and which rows share a shape,
//     flex layout facts (direction, gap, alignment — for containers whose children move when one is hidden),
//     clipping/scrolling, CSS animations and their keyframes.
// The exporter never changes what the player sees: everything it touches is put back.
//
// Used as: const ui = createUiExport(frame); const { doc, looks } = ui.plan(); for each look: ui.shoot(i) →
// screenshot of the returned clip → ui.unshoot(); ... ui.done(). See bot.mjs doExport().

const PAD = 48; // how far around an element's box its picture is taken (shadows and rings reach outside the box)

const q2 = (v) => Math.round(v * 100) / 100 || 0;
const half = (v) => Math.round(v * 2) / 2;
const px = (v) => parseFloat(v) || 0;

function color(c) {
  const m = c && c.match(/rgba?\(([^)]+)\)/);
  if (!m) return null;
  const [r, g, b, a = 1] = m[1].split(/[ ,/]+/).filter(Boolean).map(Number);
  return a === 0 ? null : { r: r / 255, g: g / 255, b: b / 255, a: +a };
}

/** a CSS filter list applied to a colour (what a filtered parent does to the text inside it) */
function filtered(c, filters, lint) {
  if (!c) return c;
  let { r, g, b } = c;
  for (const f of filters) {
    for (const m of f.matchAll(/([a-z-]+)\(([^)]*)\)/g)) {
      const v = m[2].trim().endsWith('%') ? parseFloat(m[2]) / 100 : parseFloat(m[2]);
      if (m[1] === 'brightness') [r, g, b] = [r * v, g * v, b * v];
      else if (m[1] === 'saturate' || m[1] === 'grayscale') {
        const s = m[1] === 'saturate' ? v : 1 - Math.min(1, v);
        [r, g, b] = [
          (0.213 + 0.787 * s) * r + (0.715 - 0.715 * s) * g + (0.072 - 0.072 * s) * b,
          (0.213 - 0.213 * s) * r + (0.715 + 0.285 * s) * g + (0.072 - 0.072 * s) * b,
          (0.213 - 0.213 * s) * r + (0.715 - 0.715 * s) * g + (0.072 + 0.928 * s) * b,
        ];
      } else lint(`filter ${m[1]}() on text is not exported (its colour is taken unfiltered)`);
    }
  }
  const cl = (v) => Math.min(1, Math.max(0, v));
  return { r: cl(r), g: cl(g), b: cl(b), a: c.a };
}

const DECO = ['backgroundColor', 'backgroundImage', 'backgroundSize', 'backgroundPosition', 'backgroundRepeat', 'backgroundClip', 'backgroundOrigin',
  'borderTopWidth', 'borderRightWidth', 'borderBottomWidth', 'borderLeftWidth', 'borderTopStyle', 'borderRightStyle', 'borderBottomStyle', 'borderLeftStyle',
  'borderTopColor', 'borderRightColor', 'borderBottomColor', 'borderLeftColor',
  'borderTopLeftRadius', 'borderTopRightRadius', 'borderBottomRightRadius', 'borderBottomLeftRadius',
  'boxShadow', 'filter', 'clipPath', 'outlineStyle', 'outlineWidth', 'outlineColor', 'mixBlendMode', 'transform'];
/** a transform that only moves the element does not change its picture */
const turns = (cs) => {
  if (cs.transform === 'none') return false;
  const m = cs.transform.match(/^matrix\(([^)]+)\)$/);
  if (!m) return true;
  const [a, b, c, d] = m[1].split(',').map(Number);
  return Math.abs(a - 1) > 1e-6 || Math.abs(d - 1) > 1e-6 || Math.abs(b) > 1e-6 || Math.abs(c) > 1e-6;
};
/** ...and a picture that is only made bigger or smaller is still that picture (turned or mirrored, it is not) */
const rotates = (cs) => {
  if (cs.transform === 'none') return false;
  const m = cs.transform.match(/^matrix\(([^)]+)\)$/);
  if (!m) return true;
  const [a, b, c, d] = m[1].split(',').map(Number);
  return Math.abs(b) > 1e-6 || Math.abs(c) > 1e-6 || a <= 0 || d <= 0;
};
const decoSig = (cs) => DECO.map((k) => (k === 'transform' ? (turns(cs) ? cs.transform : '') : cs[k])).join(';');

/** does CSS paint anything for this box (not counting its text and its children)? */
function paints(cs) {
  if (color(cs.backgroundColor) || cs.backgroundImage !== 'none' || cs.boxShadow !== 'none') return true;
  for (const s of ['Top', 'Right', 'Bottom', 'Left']) if (px(cs[`border${s}Width`]) > 0 && cs[`border${s}Style`] !== 'none' && color(cs[`border${s}Color`])) return true;
  return cs.outlineStyle !== 'none' && px(cs.outlineWidth) > 0;
}
const pseudoOn = (p) => p.content !== 'none' && p.content !== 'normal' && p.display !== 'none';

/** selector list → selectors; selector → compound parts with the combinator before each */
function splitTop(s, seps) {
  const out = [];
  let depth = 0;
  let cur = '';
  let quote = '';
  for (const ch of s) {
    if (quote) {
      cur += ch;
      if (ch === quote) quote = '';
      continue;
    }
    if (ch === '"' || ch === "'") quote = ch;
    if (ch === '(' || ch === '[') depth++;
    if (ch === ')' || ch === ']') depth--;
    if (depth === 0 && seps.includes(ch)) {
      out.push([cur, ch]);
      cur = '';
    } else cur += ch;
  }
  out.push([cur, '']);
  return out;
}

export function createUiExport(frame) {
  const root = frame.querySelector('[data-ui-states]') ?? frame;
  const STATES = (root.dataset.uiStates ?? '').split(/\s+/).filter(Boolean);
  const isState = new Set(STATES);
  const lintSet = new Set();
  const lint = (msg) => lintSet.add(msg);
  let fr = frame.getBoundingClientRect();
  let u = fr.width / 1080;
  const undo = []; // everything changed for the export, put back by done()

  /** finite animations and transitions jump to their end, endless ones stop: the page shows its resting look */
  const settle = () => {
    for (const a of document.getAnimations()) {
      const t = a.effect?.getComputedTiming?.();
      if (t && t.iterations === Infinity) a.cancel();
      else {
        try {
          a.finish();
        } catch {
          a.cancel();
        }
      }
    }
  };

  /** show an element that is hidden (it, and whatever hides it above); returns how to hide it again */
  function reveal(el) {
    const was = [];
    for (let e = el; e && e !== frame; e = e.parentElement) {
      if (e.hidden) {
        was.push(e);
        e.hidden = false;
      }
    }
    return () => was.forEach((e) => (e.hidden = true));
  }

  const images = {}; // image file -> where the web loads it from
  // ---- looks: one entry per distinct picture to take
  const looks = [];
  const lookIndex = new Map();

  /** ancestors that change how an element is drawn: filters, and boxes that clip it */
  function context(el, r, reach = 0) {
    const filters = [];
    const clips = [];
    const clipEls = [];
    for (let a = el.parentElement; a && a !== frame.parentElement; a = a.parentElement) {
      const cs = getComputedStyle(a);
      if (cs.filter !== 'none') filters.push(cs.filter);
      const ox = cs.overflowX;
      const oy = cs.overflowY;
      if (ox === 'visible' && oy === 'visible') continue;
      if (ox === 'auto' || ox === 'scroll' || oy === 'auto' || oy === 'scroll') continue; // a scrolling list: Unity masks it itself
      if (a === frame) continue;
      // the clipping box (inside the border) with its inner corner radii. The element is cut when it reaches outside
      // that box, or when one of its corners lies in the rounded-off part of the box's corner
      const ar = a.getBoundingClientRect();
      const bl = px(cs.borderLeftWidth), bt = px(cs.borderTopWidth), br = px(cs.borderRightWidth), bb = px(cs.borderBottomWidth);
      const x0 = ar.left + bl, y0 = ar.top + bt, x1 = ar.right - br, y1 = ar.bottom - bb;
      const eps = 0.01;
      const L = r.left - reach, T = r.top - reach, R = r.right + reach, B = r.bottom + reach;
      let cut = L < x0 - eps || T < y0 - eps || R > x1 + eps || B > y1 + eps;
      const corner = (rad, bx, by, cx, cy, ex, ey) => {
        // (cx, cy): centre of the corner's circle; (ex, ey): the element's corner on that side
        const ir = Math.max(0, px(rad) - Math.min(bx, by));
        if (ir <= 0) return;
        const dx = (ex - cx) * Math.sign(cx - (x0 + x1) / 2 || 1), dy = (ey - cy) * Math.sign(cy - (y0 + y1) / 2 || 1);
        if (dx > 0 && dy > 0 && Math.hypot(dx, dy) > ir + eps) cut = true;
      };
      const ir = (rad, bx, by) => Math.max(0, px(rad) - Math.min(bx, by));
      corner(cs.borderTopLeftRadius, bl, bt, x0 + ir(cs.borderTopLeftRadius, bl, bt), y0 + ir(cs.borderTopLeftRadius, bl, bt), L, T);
      corner(cs.borderTopRightRadius, br, bt, x1 - ir(cs.borderTopRightRadius, br, bt), y0 + ir(cs.borderTopRightRadius, br, bt), R, T);
      corner(cs.borderBottomRightRadius, br, bb, x1 - ir(cs.borderBottomRightRadius, br, bb), y1 - ir(cs.borderBottomRightRadius, br, bb), R, B);
      corner(cs.borderBottomLeftRadius, bl, bb, x0 + ir(cs.borderBottomLeftRadius, bl, bb), y1 - ir(cs.borderBottomLeftRadius, bl, bb), L, B);
      if (cut) clipEls.push(a);
      if (cut) clips.push([half((x0 - r.left) / u), half((y0 - r.top) / u), half((x1 - r.right) / u), half((y1 - r.bottom) / u), cs.borderTopLeftRadius, cs.borderTopRightRadius, cs.borderBottomRightRadius, cs.borderBottomLeftRadius, bl, bt].join(','));
    }
    return { filters, clips, clipEls };
  }

  /** children that have no data-ui belong to the picture of the nearest named element above them */
  function ownChildren(el, out = []) {
    for (const c of el.children) {
      if (c.dataset.ui || c.hasAttribute('data-ui-screen')) continue;
      out.push(c);
      ownChildren(c, out);
    }
    return out;
  }

  /**
   * How an element looks, as a key: equal keys = the same picture.
   * @returns {{ kind: 'none'|'flat'|'image'|'skin', sig?: string, hint?: string }}
   */
  function lookOf(el, cs, r, screenName) {
    const isImg = el.tagName === 'IMG';
    const before = getComputedStyle(el, '::before');
    const after = getComputedStyle(el, '::after');
    const own = ownChildren(el);
    let ink = isImg || paints(cs) || pseudoOn(before) || pseudoOn(after);
    const ownSig = [];
    for (const c of own) {
      const ccs = getComputedStyle(c);
      if (ccs.display === 'none') continue;
      const text = [...c.childNodes].filter((n) => n.nodeType === 3).map((n) => n.textContent).join('').trim();
      const cInk = c.tagName === 'IMG' || paints(ccs) || pseudoOn(getComputedStyle(c, '::before')) || pseudoOn(getComputedStyle(c, '::after'));
      if (text) lint(`${screenName}: text "${text.slice(0, 24)}" sits in an element without data-ui (inside ${el.dataset.ui ?? el.dataset.uiScreen}): give it data-ui, or Unity gets it as a picture`);
      if (cInk || text) {
        ink = true;
        if (cInk) lint(`${screenName}: <${c.tagName.toLowerCase()} class="${c.className}"> inside ${el.dataset.ui ?? el.dataset.uiScreen} has no data-ui: it is baked into its parent's picture`);
        const cr = c.getBoundingClientRect();
        ownSig.push([c.tagName, c.getAttribute('src') ?? '', half((cr.left - r.left) / u), half((cr.top - r.top) / u), half(cr.width / u), half(cr.height / u), decoSig(ccs), text, ccs.color, ccs.fontSize].join(';'));
      }
    }
    if (!ink || r.width < 0.01 || r.height < 0.01) return { kind: 'none' };
    // a shadow reaches outside the box: a clipping ancestor may cut it even when the box itself is clear
    const ctx = context(el, r, cs.boxShadow !== 'none' ? PAD * u * 0.5 : 0);
    const moved = turns(cs);
    const plain = !isImg && !ownSig.length && !pseudoOn(before) && !pseudoOn(after) && !ctx.filters.length && !ctx.clips.length && cs.filter === 'none' && !moved;
    if (plain && cs.backgroundImage === 'none' && cs.boxShadow === 'none' && px(cs.borderTopLeftRadius) + px(cs.borderTopRightRadius) + px(cs.borderBottomRightRadius) + px(cs.borderBottomLeftRadius) === 0) {
      let border = false;
      for (const s of ['Top', 'Right', 'Bottom', 'Left']) if (px(cs[`border${s}Width`]) > 0 && cs[`border${s}Style`] !== 'none' && color(cs[`border${s}Color`])) border = true;
      if (!border && cs.outlineStyle === 'none') return { kind: 'flat' };
    }
    // a picture file shown as it is: an <img>, or a box painted by nothing but one stretched background picture
    // (`background: url(x.png) center / 100% 100%`) — Unity shows the file itself, at any size
    const bgUrl = (cs.backgroundImage.match(/^url\("?(.*?)"?\)$/) || [])[1] || null;
    let border = false;
    for (const s of ['Top', 'Right', 'Bottom', 'Left']) if (px(cs[`border${s}Width`]) > 0 && cs[`border${s}Style`] !== 'none' && color(cs[`border${s}Color`])) border = true;
    const bgPicture = !isImg && !!bgUrl && !color(cs.backgroundColor) && cs.boxShadow === 'none' && !border && cs.outlineStyle === 'none' && cs.backgroundSize === '100% 100%' &&
      px(cs.borderTopLeftRadius) + px(cs.borderTopRightRadius) + px(cs.borderBottomRightRadius) + px(cs.borderBottomLeftRadius) === 0 && !ownSig.length && !pseudoOn(before) && !pseudoOn(after);
    const asIs = cs.filter === 'none' && !ctx.filters.length && !ctx.clips.length && !rotates(cs);
    if (bgPicture && asIs) return { kind: 'image', file: new URL(bgUrl, location.href).pathname.replace(/^\//, '') };
    const untouched = isImg && asIs;
    if (untouched && !paints(cs)) return { kind: 'image', file: el.getAttribute('src') };
    const pseudo = (p) => (pseudoOn(p) ? [p.content, decoSig(p), p.position, p.left, p.top, p.right, p.bottom, p.width, p.height].join(';') : '');
    // an <img> with a frame of its own (a background, a border): the frame is the skin, the picture stays the image
    const frameOnly = untouched;
    const sig = [isImg && !frameOnly ? 'img:' + el.getAttribute('src') : '', half(r.width / u), half(r.height / u), decoSig(cs), pseudo(before), pseudo(after), ownSig.join('/'), ctx.filters.join('/'), ctx.clips.join('/')].join('|');
    const cls = [...el.classList].filter((c) => !isState.has(c)).join('_');
    const file = (isImg && !frameOnly) || bgPicture ? (el.getAttribute('src') ?? bgUrl ?? '').split('/').pop().replace(/\.\w+$/, '') : '';
    // A picture whose BOX reaches into a clipping corner is usually not cut at all (its own corners are empty). Only the
    // pixels can tell: bot.mjs photographs it once more with that clipping off, and if nothing changes it stays the file.
    const fileUrl = isImg ? el.getAttribute('src') : bgUrl ? new URL(bgUrl, location.href).pathname.replace(/^\//, '') : null;
    const plainIf = (bgPicture || (isImg && !paints(cs))) && cs.filter === 'none' && !ctx.filters.length && !rotates(cs) && ctx.clips.length ? { file: fileUrl, clipEls: ctx.clipEls } : null;
    return { kind: 'skin', sig, frameOnly, plainIf, picture: (isImg && !frameOnly) || bgPicture, hint: (file || cls || (el.dataset.ui ?? el.tagName).split('.').pop()).replace(/[^a-zA-Z0-9_]+/g, '_') };
  }

  /** the look as a number: the same number = the same picture; remembers how to take that picture */
  function lookId(el, look, host, combo) {
    let i = lookIndex.get(look.sig);
    if (i == null) {
      i = looks.length;
      lookIndex.set(look.sig, i);
      looks.push({ el, host, combo, frameOnly: look.frameOnly, picture: look.picture, plainIf: look.plainIf, hint: look.hint + (combo ? '_' + combo.join('_') : '') });
    }
    return i;
  }

  // ---- text
  function textOf(el, cs, boxEl, lone, ctxFilters) {
    const nodes = [...el.childNodes].filter((n) => n.nodeType === 3 && n.textContent.trim());
    if (!nodes.length) return null;
    const text = nodes.map((n) => n.textContent).join('').trim();
    const range = document.createRange();
    range.setStartBefore(nodes[0]);
    range.setEndAfter(nodes[nodes.length - 1]);
    const lines = [...range.getClientRects()].filter((r) => r.width > 0);
    if (!lines.length) return { text, font: fontOf(cs, ctxFilters, 'center', 'middle'), box: null };
    const L = Math.min(...lines.map((r) => r.left)), T = Math.min(...lines.map((r) => r.top));
    const R = Math.max(...lines.map((r) => r.right)), B = Math.max(...lines.map((r) => r.bottom));
    // the box the text lives in: its own box, or — a lone <span> in a box — that box (so a longer text at runtime
    // stays where the web keeps it: centred, or against its side)
    let box = null;
    let align = 'center';
    let valign = 'middle';
    if (lone) {
      const bcs = getComputedStyle(boxEl);
      const br = boxEl.getBoundingClientRect();
      const x0 = br.left + px(bcs.borderLeftWidth) + px(bcs.paddingLeft), x1 = br.right - px(bcs.borderRightWidth) - px(bcs.paddingRight);
      const y0 = br.top + px(bcs.borderTopWidth) + px(bcs.paddingTop), y1 = br.bottom - px(bcs.borderBottomWidth) - px(bcs.paddingBottom);
      box = { x: x0, y: y0, w: x1 - x0, h: y1 - y0 };
      // where the text sits in that box: measured, so flex centring, text-align and padding all come out right
      const tol = Math.max(1.5 * u, 0.02 * box.w);
      const dl = L - x0, dr = x1 - R;
      align = Math.abs(dl - dr) <= tol ? 'center' : dl < dr ? 'left' : 'right';
      const dt = T - y0, db = y1 - B;
      valign = Math.abs(dt - db) <= Math.max(1.5 * u, 0.04 * box.h) ? 'middle' : dt < db ? 'top' : 'bottom';
    }
    return { text, font: fontOf(cs, ctxFilters, align, valign), box, ink: { x: L, y: T, w: R - L, h: B - T }, lines: lines.length };
  }

  function fontOf(cs, ctxFilters, align, valign) {
    const size = px(cs.fontSize) / u;
    const f = {
      family: cs.fontFamily.split(',')[0].replace(/["']/g, '').trim(),
      size: q2(size),
      color: filtered(color(cs.color), ctxFilters, lint),
      bold: parseInt(cs.fontWeight, 10) >= 600,
      align,
    };
    if (valign !== 'middle') f.valign = valign;
    if (cs.letterSpacing !== 'normal' && px(cs.letterSpacing)) f.spacing = q2(px(cs.letterSpacing) / u);
    if (cs.lineHeight !== 'normal') f.lineHeight = q2(px(cs.lineHeight) / u);
    if (cs.whiteSpace !== 'nowrap' && cs.whiteSpace !== 'pre') f.wrap = true;
    if (cs.textTransform === 'uppercase') f.upper = true;
    if (cs.textShadow && cs.textShadow !== 'none') {
      // "rgba(0, 0, 0, 0.5) 0px 1.5px 0px[, ...]": the first shadow
      const first = splitTop(cs.textShadow, ',')[0][0].trim();
      const c = color(first);
      const nums = first.replace(/rgba?\([^)]*\)/, '').trim().split(/\s+/).map(px);
      if (c) f.shadow = { x: q2(nums[0] / u), y: q2(nums[1] / u), blur: q2((nums[2] || 0) / u), color: c };
      if (splitTop(cs.textShadow, ',').length > 1) lint('a text has more than one text-shadow: only the first is exported');
    }
    return f;
  }

  // ---- one element
  /** @param ctx { screen, parent (node) , host, combo } */
  function describe(el, name, parent, ctx) {
    const cs = getComputedStyle(el);
    if (cs.display === 'none') return { el, name, parent: parent ? parent.name : null, gone: true, x: 0, y: 0, w: 0, h: 0 };
    const r = el.getBoundingClientRect();
    const node = { el, name, parent: parent ? parent.name : null, x: q2((r.left - fr.left) / u), y: q2((r.top - fr.top) / u), w: q2(r.width / u), h: q2(r.height / u) };
    const look = lookOf(el, cs, r, ctx.screen);
    const cx = context(el, r);
    // (kept for importers that do not use skins: a flat colour, rounded by `radius`)
    node.bg = color(cs.backgroundColor);
    node.radius = q2(px(cs.borderTopLeftRadius) / u);
    node.image = look.kind === 'image' ? look.file.split('/').pop() : look.frameOnly ? el.getAttribute('src').split('/').pop() : null;
    if (look.frameOnly) {
      // where the picture itself is drawn: inside the frame's border and padding
      const il = px(cs.borderLeftWidth) + px(cs.paddingLeft), it = px(cs.borderTopWidth) + px(cs.paddingTop);
      const iw = r.width - il - px(cs.borderRightWidth) - px(cs.paddingRight), ih = r.height - it - px(cs.borderBottomWidth) - px(cs.paddingBottom);
      node.ibox = [q2((r.left + il - fr.left) / u), q2((r.top + it - fr.top) / u), q2(iw / u), q2(ih / u)];
    }
    if (look.kind === 'skin') node.look = lookId(el, look, ctx.host, ctx.combo);
    if (node.image) images[node.image] = look.kind === 'image' ? look.file : el.getAttribute('src');
    // text: own text nodes. A lone <span> (nothing else flows beside it) takes its parent's box
    const p = el.parentElement;
    const isSpan = el.tagName === 'SPAN';
    let lone = !isSpan;
    let boxEl = el;
    if (isSpan && parent && p === parent.el) {
      const beside = [...p.childNodes].filter((n) => n !== el && ((n.nodeType === 3 && n.textContent.trim()) || (n.nodeType === 1 && !['absolute', 'fixed'].includes(getComputedStyle(n).position) && getComputedStyle(n).display !== 'none')));
      lone = beside.length === 0;
      boxEl = p;
    }
    const t = textOf(el, cs, boxEl, lone, [cs.filter, ...cx.filters].filter((f) => f !== 'none'));
    node.text = t ? t.text : null;
    node.font = t ? t.font : null;
    if (t) {
      // the text object's rectangle: the box (see above), else the text itself, centred on where it is drawn. An
      // element that has a picture of its own keeps its box, and says where its text goes in `tbox`
      const b = t.box ?? t.ink ?? { x: r.left, y: r.top, w: r.width, h: r.height };
      const tb = { x: q2((b.x - fr.left) / u), y: q2((b.y - fr.top) / u), w: q2(b.w / u), h: q2(b.h / u) };
      if (look.kind === 'none') Object.assign(node, tb);
      else node.tbox = [tb.x, tb.y, tb.w, tb.h];
      if (t.ink) node.ink = [q2((t.ink.x - fr.left) / u), q2((t.ink.y - fr.top) / u), q2(t.ink.w / u), q2(t.ink.h / u)];
      if (t.lines > 1) node.lines = t.lines;
    }
    node.button = el.dataset.uiButton || null;
    node.opacity = q2(cs.opacity);
    if (cs.zIndex !== 'auto' && cs.position !== 'static') node.z = +cs.zIndex; // drawn over its later siblings
    if (cs.animationName && cs.animationName !== 'none') {
      const list = (k) => cs[k].split(',').map((s) => s.trim());
      node.anim = list('animationName').map((n, i) => ({ name: n, duration: parseFloat(list('animationDuration')[i] ?? list('animationDuration')[0]), timing: splitTop(cs.animationTimingFunction, ',')[i]?.[0].trim() ?? 'ease', iterations: list('animationIterationCount')[i] ?? '1', direction: list('animationDirection')[i] ?? 'normal' }));
    }
    const scrolls = ['auto', 'scroll'].includes(cs.overflowY) ? 'y' : ['auto', 'scroll'].includes(cs.overflowX) ? 'x' : null;
    if (scrolls) node.scroll = scrolls;
    if (scrolls || cs.overflowX === 'hidden' || cs.overflowY === 'hidden') node.clip = true;
    // flex facts: which way the children flow and how they are spread (Unity needs them where a hidden child or a
    // longer text moves its neighbours)
    if (cs.display.includes('flex')) {
      const row = cs.flexDirection.startsWith('row');
      node.layout = { dir: row ? 'row' : 'column', justify: cs.justifyContent, align: cs.alignItems, gap: q2(px(row ? cs.columnGap : cs.rowGap) / u) };
      if (cs.flexWrap !== 'nowrap') node.layout.wrap = true;
      const pad = [px(cs.paddingTop) + px(cs.borderTopWidth), px(cs.paddingRight) + px(cs.borderRightWidth), px(cs.paddingBottom) + px(cs.borderBottomWidth), px(cs.paddingLeft) + px(cs.borderLeftWidth)].map((v) => q2(v / u));
      if (pad.some((v) => v)) node.layout.pad = pad;
      // a placed (absolute) flex box grows with its content away from the edge CSS pins it by (top or bottom, left or
      // right, or its middle): grow it a little and see which edge stays. Unity anchors it there, or a taller text
      // would push a card pinned by its bottom down over what is below it. [x, y]: 0 left/top, 0.5 middle, 1 right/bottom
      if (['absolute', 'fixed'].includes(cs.position)) {
        const props = ['min-width', 'min-height'];
        const was = props.map((k) => [el.style.getPropertyValue(k), el.style.getPropertyPriority(k)]);
        el.style.setProperty('min-width', `${r.width + 40}px`, 'important');
        el.style.setProperty('min-height', `${r.height + 40}px`, 'important');
        const g = el.getBoundingClientRect();
        props.forEach((k, i) => (was[i][0] ? el.style.setProperty(k, was[i][0], was[i][1]) : el.style.removeProperty(k)));
        const edge = (a0, a1, b0, b1) => (Math.abs(b0 - a0) < 0.5 ? 0 : Math.abs(b1 - a1) < 0.5 ? 1 : Math.abs(b0 - a0 - (a1 - b1)) < 1 ? 0.5 : null);
        const pin = [edge(r.left, r.right, g.left, g.right), edge(r.top, r.bottom, g.top, g.bottom)];
        if (pin.some((v) => v !== null)) node.pin = pin;
      }
    }
    if (p && getComputedStyle(p).display.includes('flex') && !['absolute', 'fixed'].includes(cs.position)) {
      const flow = {};
      if (+cs.flexGrow) flow.grow = +cs.flexGrow;
      if (+cs.order) flow.order = +cs.order;
      const m = [cs.marginTop, cs.marginRight, cs.marginBottom, cs.marginLeft].map((v) => q2(px(v) / u));
      if (m.some((v) => v)) flow.margin = m;
      if (cs.alignSelf !== 'auto' && cs.alignSelf !== 'normal') flow.self = cs.alignSelf;
      // a transform (scale, translate) changes the measured box but not the room the element takes among its
      // siblings: give that room too (the box with the transform switched off), or Unity's flexbox mis-places them
      if (cs.transform !== 'none' || (cs.scale && cs.scale !== 'none') || (cs.translate && cs.translate !== 'none')) {
        const props = ['transform', 'scale', 'translate'];
        const was = props.map((k) => [el.style.getPropertyValue(k), el.style.getPropertyPriority(k)]);
        for (const k of props) el.style.setProperty(k, 'none', 'important');
        const l = el.getBoundingClientRect();
        props.forEach((k, i) => (was[i][0] ? el.style.setProperty(k, was[i][0], was[i][1]) : el.style.removeProperty(k)));
        const lb = [q2((l.left - fr.left) / u), q2((l.top - fr.top) / u), q2(l.width / u), q2(l.height / u)];
        if (lb[0] !== node.x || lb[1] !== node.y || lb[2] !== node.w || lb[3] !== node.h) flow.lbox = lb;
      }
      node.flow = flow;
    }
    if (el.dataset.uiFill) {
      // which side the bar grows from: shrink it and see which edge stays
      const axis = el.dataset.uiFill === 'y' ? 'y' : 'x';
      const prop = axis === 'x' ? 'width' : 'height';
      el.style.setProperty(prop, '40%', 'important');
      const s = el.getBoundingClientRect();
      el.style.setProperty(prop, '100%', 'important');
      const from = axis === 'x' ? (Math.abs(s.left - r.left) < 0.5 ? 'left' : 'right') : Math.abs(s.top - r.top) < 0.5 ? 'top' : 'bottom';
      // a bar with its own round ends keeps them at any length (Unity: resize a sliced image); a square one is cut by
      // its rounded frame (Unity: a filled image of the full picture)
      node.fill = { axis, from, mode: px(cs.borderTopLeftRadius) > 0 ? 'resize' : 'reveal' };
    }
    return node;
  }

  /** an element and everything named below it. A hidden element is shown only while it is measured. */
  function walk(el, parent, ctx, nodes) {
    const hidden = el.hidden;
    if (hidden) {
      el.hidden = false;
      settle();
    }
    let me = parent;
    const name = el.dataset.ui || (el === ctx.screenEl ? ctx.screen : '');
    if (name) {
      me = describe(el, name, parent, ctx);
      if (hidden) me.hidden = true;
      if (el.hasAttribute('data-ui-list')) me.isList = true;
      nodes.push(me);
    }
    for (const c of el.children) walk(c, me, ctx, nodes);
    if (hidden) el.hidden = true;
    return nodes;
  }

  // ---- states: which elements the code can put into which state (read from the style sheets)
  function stateHosts() {
    const hosts = new Map();
    const visit = (rules) => {
      for (const rule of rules) {
        if (rule.cssRules && !rule.selectorText) visit(rule.cssRules);
        if (!rule.selectorText) continue;
        for (const [sel] of splitTop(rule.selectorText, ',')) {
          for (const S of STATES) {
            const re = new RegExp(`\\.${S}(?![\\w-])`);
            if (!re.test(sel)) continue;
            // compounds with the combinator after each; the host is the compound that carries the state class
            const parts = splitTop(sel.trim().replace(/\s*([>+~])\s*/g, '$1'), ' >+~');
            for (let i = 0; i < parts.length; i++) {
              if (!re.test(parts[i][0])) continue;
              let hostSel = '';
              for (let k = 0; k <= i; k++) hostSel += (k === i ? parts[k][0].replace(re, '').replace(/::?(before|after)$/, '') : parts[k][0]) + (k < i ? parts[k][1] : '');
              let found = [];
              try {
                found = frame.querySelectorAll(hostSel.replace(/:(active|hover|focus)/g, ''));
              } catch {
                lint(`could not read the selector "${sel}"`);
              }
              for (const el of found) {
                if (!el.dataset.ui) continue;
                if (!hosts.has(el)) hosts.set(el, new Set());
                hosts.get(el).add(S);
              }
            }
          }
        }
      }
    };
    for (const sheet of document.styleSheets) {
      try {
        visit(sheet.cssRules);
      } catch {
        /* a sheet from another origin: nothing of ours */
      }
    }
    return hosts;
  }

  const combosOf = (list) => {
    const out = [];
    const n = list.length;
    for (let mask = 1; mask < 1 << n; mask++) {
      const c = list.filter((_, i) => mask & (1 << i));
      if (n <= 3 || c.length <= 2) out.push(c);
    }
    return out;
  };

  const SAME = ['look', 'image', 'text', 'opacity', 'gone', 'z'];
const SAME_JSON = ['font', 'anim', 'tbox', 'ibox', 'pin'];
  /** what a state changes on a node (compared with its resting description) */
  function diff(base, now, baseParent, nowParent) {
    if (now.gone) return base.gone ? null : { gone: true }; // not shown at all in this state: nothing else to say
    const d = {};
    for (const k of SAME) if ((base[k] ?? null) !== (now[k] ?? null)) d[k] = now[k] ?? null;
    if (JSON.stringify(base.bg) !== JSON.stringify(now.bg) && now.look == null) d.bg = now.bg;
    for (const k of SAME_JSON) if (JSON.stringify(base[k] ?? null) !== JSON.stringify(now[k] ?? null) && !(k === 'tbox' || k === 'ibox')) d[k] = now[k] ?? null;
    // moved or resized inside its parent
    const rel = (n, p) => [q2(n.x - (p ? p.x : 0)), q2(n.y - (p ? p.y : 0)), n.w, n.h];
    const a = rel(base, baseParent), b = rel(now, nowParent);
    if (a.some((v, i) => Math.abs(v - b[i]) > 0.05) && !now.gone) Object.assign(d, { x: now.x, y: now.y, w: now.w, h: now.h });
    if (d.x != null && now.tbox) d.tbox = now.tbox;
    if (d.x != null && now.ibox) d.ibox = now.ibox;
    return Object.keys(d).length ? d : null;
  }

  // ---- the whole description
  function plan() {
    fr = frame.getBoundingClientRect();
    u = fr.width / 1080;
    settle();
    // bars at full length, state classes off (what was on is reported as `initial`)
    for (const el of frame.querySelectorAll('[data-ui-fill]')) {
      const prop = el.dataset.uiFill === 'y' ? 'height' : 'width';
      const was = [el.style.getPropertyValue(prop), el.style.getPropertyPriority(prop)];
      el.style.setProperty(prop, '100%', 'important');
      undo.push(() => (was[0] ? el.style.setProperty(prop, was[0], was[1]) : el.style.removeProperty(prop)));
    }
    const hosts = stateHosts();
    const initial = [];
    for (const el of frame.querySelectorAll('[class]')) {
      const on = STATES.filter((s) => el.classList.contains(s));
      if (!on.length) continue;
      on.forEach((s) => el.classList.remove(s));
      undo.push(() => on.forEach((s) => el.classList.add(s)));
      if (el.dataset.ui) initial.push({ host: el.dataset.ui, on });
      if (!hosts.has(el)) hosts.set(el, new Set());
      on.forEach((s) => hosts.get(el).add(s));
    }
    settle();

    const screens = [];
    const byEl = new Map(); // element -> its resting node
    const byName = new Map();
    for (const scr of frame.querySelectorAll('[data-ui-screen]')) {
      const ctx = { screen: scr.dataset.uiScreen, screenEl: scr, host: null, combo: null };
      const nodes = walk(scr, null, ctx, []);
      nodes.forEach((n) => (byEl.set(n.el, n), byName.set(n.name, n)));
      screens.push({ screen: ctx.screen, startsHidden: scr.hidden, nodes });
    }

    // lists: which rows have the same shape (Unity makes one row prefab per shape)
    for (const s of screens) {
      for (const list of s.nodes.filter((n) => n.isList)) {
        const items = s.nodes.filter((n) => n.parent === list.name);
        const shapes = new Map();
        for (const it of items) {
          const below = s.nodes.filter((n) => n.name.startsWith(it.name + '.')).map((n) => n.name.slice(it.name.length)).sort();
          const key = below.length ? below.join(',') : 'leaf:' + [...it.el.classList].filter((c) => !isState.has(c)).join('.');
          if (!shapes.has(key)) shapes.set(key, []);
          shapes.get(key).push(it);
        }
        let k = 0;
        list.list = [];
        for (const group of shapes.values()) {
          const names = group.map((n) => n.name);
          let pre = names[0];
          for (const n of names) while (!n.startsWith(pre)) pre = pre.slice(0, -1);
          if (names.length === 1) pre = names[0].slice(0, names[0].lastIndexOf('.') + 1);
          const tpl = `${list.name}#${k++}`;
          group.forEach((n) => (n.item = { tpl, key: n.name.slice(pre.length) }));
          list.list.push({ tpl, prefix: pre, count: group.length, sample: names[0] });
        }
        delete list.isList;
      }
    }

    // states
    const states = [];
    for (const [host, set] of hosts) {
      const base = byEl.get(host);
      if (!base) continue;
      const scr = host.closest('[data-ui-screen]');
      const list = STATES.filter((s) => set.has(s));
      for (const combo of combosOf(list)) {
        const hide = reveal(host);
        combo.forEach((s) => host.classList.add(s));
        settle();
        const ctx = { screen: scr.dataset.uiScreen, screenEl: scr, host, combo };
        const parentNode = base.parent ? byEl.get(host.parentElement.closest('[data-ui], [data-ui-screen]')) : null;
        const now = walk(host, parentNode ? { ...parentNode } : null, ctx, []);
        const nowBy = new Map(now.map((n) => [n.name, n]));
        const change = {};
        for (const n of now) {
          const b = byEl.get(n.el);
          if (!b) continue;
          const d = diff(b, n, b.parent ? byName.get(b.parent) ?? null : null, n.parent ? nowBy.get(n.parent) ?? parentNode : null);
          if (d) change[n.name] = d;
        }
        combo.forEach((s) => host.classList.remove(s));
        hide();
        settle();
        if (Object.keys(change).length) states.push({ host: base.name, on: combo, set: change });
      }
    }

    // keyframes, as written (calc(N * var(--u)) → N design pixels)
    const keyframes = {};
    const frames = (rules) => {
      for (const rule of rules) {
        if (rule.type === CSSRule.KEYFRAMES_RULE) {
          keyframes[rule.name] = [...rule.cssRules].map((k) => {
            const style = {};
            for (const prop of k.style) style[prop] = k.style.getPropertyValue(prop).replace(/calc\(\s*(-?[\d.]+)\s*\*\s*var\(--u\)\s*\)/g, '$1px');
            return { at: k.keyText, style };
          });
        } else if (rule.cssRules) frames(rule.cssRules);
      }
    };
    for (const sheet of document.styleSheets) {
      try {
        frames(sheet.cssRules);
      } catch {
        /* another origin */
      }
    }

    const clean = (n) => {
      const { el, ...rest } = n;
      return rest;
    };
    const doc = {
      version: 2,
      refWidth: 1080,
      refHeight: Math.round(fr.height / u),
      screens: screens.map((s) => ({ ...s, nodes: s.nodes.map(clean) })),
      states,
      initial,
      images,
      keyframes,
      lint: [...lintSet],
    };
    // a picture that must stay whole: an icon, and a bar that is revealed bit by bit (Unity fills it from one side)
    const whole = (l) => l.picture || (!!l.el.dataset.uiFill && !(px(getComputedStyle(l.el).borderTopLeftRadius) > 0));
    return { doc, looks: looks.map((l) => ({ hint: l.hint, picture: whole(l), plainFile: l.plainIf ? l.plainIf.file : null })) };
  }

  // ---- taking the picture of one look
  let unshot = null;
  /** @param unclipped photograph it without the clipping of the boxes around it (see lookOf: plainIf) */
  function shoot(i, unclipped = false) {
    const L = looks[i];
    const back = [];
    if (unclipped && L.plainIf) {
      for (const a of L.plainIf.clipEls) {
        const was = [a.style.getPropertyValue('overflow'), a.style.getPropertyPriority('overflow')];
        a.style.setProperty('overflow', 'visible', 'important');
        back.push(() => (was[0] ? a.style.setProperty('overflow', was[0], was[1]) : a.style.removeProperty('overflow')));
      }
    }
    back.push(reveal(L.el));
    if (L.host) {
      back.push(reveal(L.host));
      const add = L.combo.filter((s) => !L.host.classList.contains(s));
      add.forEach((s) => L.host.classList.add(s));
      back.push(() => add.forEach((s) => L.host.classList.remove(s)));
    }
    // a row far down a scrolling list is brought into view (and the list put back afterwards)
    const scrolled = [];
    for (let a = L.el.parentElement; a && a !== frame; a = a.parentElement) if (a.scrollHeight > a.clientHeight + 1 || a.scrollWidth > a.clientWidth + 1) scrolled.push([a, a.scrollTop, a.scrollLeft]);
    if (scrolled.length) {
      L.el.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
      back.push(() => scrolled.forEach(([a, t, l]) => ((a.scrollTop = t), (a.scrollLeft = l))));
    }
    L.el.setAttribute('data-t2u-show', L.frameOnly ? 'frame' : 'node');
    const own = ownChildren(L.el);
    own.forEach((c) => c.setAttribute('data-t2u-show', 'own'));
    frame.setAttribute('data-t2u-bake', '');
    bakeStyle();
    back.push(() => {
      L.el.removeAttribute('data-t2u-show');
      own.forEach((c) => c.removeAttribute('data-t2u-show'));
      frame.removeAttribute('data-t2u-bake');
    });
    settle();
    unshot = () => back.reverse().forEach((f) => f());
    const r = L.el.getBoundingClientRect();
    const pad = PAD * u;
    return { clip: { x: r.left - pad, y: r.top - pad, width: r.width + 2 * pad, height: r.height + 2 * pad }, box: { x: r.left, y: r.top, width: r.width, height: r.height }, u };
  }
  function bakeStyle() {
    let style = document.getElementById('t2u-bake');
    if (!style) {
      style = document.createElement('style');
      style.id = 't2u-bake';
      // only the element (and its unnamed children) is drawn, without its text, at full opacity, on nothing
      style.textContent = `
        [data-t2u-solo], [data-t2u-solo] * { visibility: hidden !important; }
        [data-t2u-solo] [data-t2u-keep], [data-t2u-solo] [data-t2u-keep] * { visibility: visible !important; }
        html:has([data-t2u-bake]), html:has([data-t2u-bake]) body { background: transparent !important; }
        [data-t2u-bake], [data-t2u-bake] * { visibility: hidden !important; opacity: 1 !important; }
        [data-t2u-bake] [data-t2u-show] { visibility: visible !important; }
        [data-t2u-bake] [data-t2u-show="node"] { -webkit-text-fill-color: transparent !important; text-shadow: none !important; }
        [data-t2u-bake] [data-t2u-show="frame"] { object-fit: none !important; object-position: -100000px -100000px !important; }`;
      document.head.appendChild(style);
    }
  }
  function unshoot() {
    unshot?.();
    unshot = null;
    settle();
  }

  function done() {
    unshoot();
    undo.reverse().forEach((f) => f());
    undo.length = 0;
    document.getElementById('t2u-bake')?.remove();
  }

  /** for checking the export: show one screen as it rests (or one host in a state), nothing else; returns its box */
  function pose(screen, host, combo, solo = false) {
    const back = [];
    const scrEl = frame.querySelector(`[data-ui-screen="${screen}"]`);
    for (const s of frame.querySelectorAll('[data-ui-screen]')) {
      const was = s.hidden;
      s.hidden = s !== scrEl;
      back.push(() => (s.hidden = was));
    }
    const fills = [];
    for (const el of scrEl.querySelectorAll('[data-ui-fill]')) {
      const prop = el.dataset.uiFill === 'y' ? 'height' : 'width';
      fills.push([el, prop, el.style.getPropertyValue(prop), el.style.getPropertyPriority(prop)]);
      el.style.setProperty(prop, '100%', 'important');
    }
    back.push(() => fills.forEach(([el, prop, v, pr]) => (v ? el.style.setProperty(prop, v, pr) : el.style.removeProperty(prop))));
    const stripped = [];
    for (const el of scrEl.querySelectorAll('[class]')) for (const s of STATES) if (el.classList.contains(s)) (el.classList.remove(s), stripped.push([el, s]));
    back.push(() => stripped.forEach(([el, s]) => el.classList.add(s)));
    let box = null;
    if (host) {
      const h = scrEl.querySelector(`[data-ui="${host}"]`);
      back.push(reveal(h));
      combo.forEach((s) => h.classList.add(s));
      back.push(() => combo.forEach((s) => h.classList.remove(s)));
      settle(); // (a popup that has just been shown is still growing: measure it at rest)
      // a row that a scrolling list shows only in part (or not at all) is scrolled into view; the box says where it
      // now stands (the check draws its side at the same place)
      let r = h.getBoundingClientRect();
      const scrolled = [];
      for (let a = h.parentElement; a && a !== frame; a = a.parentElement) {
        const cs = getComputedStyle(a);
        if (!['auto', 'scroll'].includes(cs.overflowY) && !['auto', 'scroll'].includes(cs.overflowX)) continue;
        const ar = a.getBoundingClientRect();
        if (r.top < ar.top - 0.5 || r.bottom > ar.bottom + 0.5 || r.left < ar.left - 0.5 || r.right > ar.right + 0.5) scrolled.push([a, a.scrollTop, a.scrollLeft]);
      }
      const r0 = r;
      if (scrolled.length) {
        h.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
        back.push(() => scrolled.forEach(([a, t, l]) => ((a.scrollTop = t), (a.scrollLeft = l))));
        r = h.getBoundingClientRect();
      }
      box = { x: r.left, y: r.top, width: r.width, height: r.height, dx: r.left - r0.left, dy: r.top - r0.top };
      if (solo) {
        // only this element and what is inside it, on nothing
        bakeStyle();
        frame.setAttribute('data-t2u-solo', '');
        h.setAttribute('data-t2u-keep', '');
        back.push(() => (frame.removeAttribute('data-t2u-solo'), h.removeAttribute('data-t2u-keep')));
      }
    }
    settle();
    unshot = () => back.reverse().forEach((f) => f());
    return box;
  }

  return { plan, shoot, unshoot, done, pose, settle };
}

/**
 * The finished ui.json drawn back in the browser by the SAME rules the Unity importer follows: boxes placed by
 * numbers, skins as 9-sliced pictures, texts with their font facts — no CSS class of the game is used. The check
 * (bot.mjs uicheck) puts this next to the real screen and counts the pixels that differ.
 * @param doc ui.json   @param screen its name   @param spriteUrl (name) => url of a baked skin
 * @param state { host, on } to draw one state entry applied (optional); @param solo draw only that host and its contents;
 * @param at { dx, dy } how far (CSS px) the solo host was scrolled from where it rests
 * @returns the element (already appended to `frame`); remove it when done
 */
export function buildPreview(frame, doc, screen, spriteUrl, state = null, solo = false, at = null) {
  const scr = doc.screens.find((s) => s.screen === screen);
  const set = state ? (doc.states.find((s) => s.host === state.host && s.on.join(' ') === state.on.join(' ')) ?? { set: {} }).set : {};
  const rootEl = document.createElement('div');
  rootEl.id = 't2u-preview';
  rootEl.style.cssText = 'position:absolute;inset:0;pointer-events:none;';
  const u = frame.getBoundingClientRect().width / doc.refWidth;
  const css = (c) => `rgba(${Math.round(c.r * 255)},${Math.round(c.g * 255)},${Math.round(c.b * 255)},${c.a})`;
  const made = new Map();
  const base = new Map(scr.nodes.map((n) => [n.name, n]));
  // A state entry gives a node's box in that state; a node it does not list keeps its place INSIDE its parent
  // (so a card that moves takes its contents along) — the same rule the Unity importer follows.
  const nodes = [];
  const byName = new Map();
  for (const b0 of scr.nodes) {
    const n = { ...b0, ...(set[b0.name] ?? {}) };
    const bp = b0.parent ? base.get(b0.parent) : null;
    const np = b0.parent ? byName.get(b0.parent) : null;
    if (set[b0.name]?.x == null && bp && np) {
      const dx = np.x - bp.x, dy = np.y - bp.y;
      n.x = b0.x + dx;
      n.y = b0.y + dy;
      if (b0.tbox) n.tbox = [b0.tbox[0] + dx, b0.tbox[1] + dy, b0.tbox[2], b0.tbox[3]];
      if (b0.ibox) n.ibox = [b0.ibox[0] + dx, b0.ibox[1] + dy, b0.ibox[2], b0.ibox[3]];
    }
    nodes.push(n);
    byName.set(n.name, n);
  }
  // a state shows the element it is switched on (the check looks at that element), nothing else that is hidden
  const shown = new Set();
  if (state) for (let n = byName.get(state.host); n; n = byName.get(n.parent)) shown.add(n.name);
  const inHost = new Set(); // `solo`: only the host and what is inside it
  for (const n of nodes) {
    if (solo && !(n.name === state.host || inHost.has(n.parent))) continue;
    if (solo) inHost.add(n.name);
    const top = solo && n.name === state.host;
    const parent = n.parent && !top ? made.get(n.parent) : null;
    if (n.parent && !top && !parent) continue; // its parent is hidden
    if ((n.hidden && n.parent && !shown.has(n.name)) || n.gone) continue; // (the screen itself is shown by whoever asks for it)
    const pn = n.parent && !top ? byName.get(n.parent) : { x: 0, y: 0 };
    const el = document.createElement('div');
    el.style.cssText = `position:absolute;box-sizing:border-box;left:${(n.x - pn.x) * u}px;top:${(n.y - pn.y) * u}px;width:${n.w * u}px;height:${n.h * u}px;`;
    if (n.opacity < 0.999) el.style.opacity = n.opacity;
    if (n.z != null) el.style.zIndex = n.z;
    if (n.scroll) el.style.overflow = 'hidden';
    if (n.skin) {
      const sp = doc.sprites[n.skin];
      const b = n.skinBox ?? [0, 0, 0, 0];
      const k = document.createElement('div');
      const sc = doc.scale;
      const [L, B, R, T] = sp.border;
      k.style.cssText = `position:absolute;box-sizing:border-box;left:${-b[0] * u}px;top:${-b[1] * u}px;width:${(n.w + b[0] + b[2]) * u}px;height:${(n.h + b[1] + b[3]) * u}px;`;
      if (L + B + R + T > 0) {
        k.style.borderStyle = 'solid';
        k.style.borderColor = 'transparent';
        k.style.borderWidth = `${(T / sc) * u}px ${(R / sc) * u}px ${(B / sc) * u}px ${(L / sc) * u}px`;
        k.style.borderImage = `url(${spriteUrl(n.skin)}) ${T} ${R} ${B} ${L} fill / ${(T / sc) * u}px ${(R / sc) * u}px ${(B / sc) * u}px ${(L / sc) * u}px / 0 stretch`;
      } else {
        k.style.background = `url(${spriteUrl(n.skin)}) 0 0 / 100% 100% no-repeat`;
      }
      el.appendChild(k);
    } else if (n.bg && !n.image) el.style.background = css(n.bg);
    if (n.image) {
      const img = document.createElement('img');
      img.src = doc.images[n.image];
      const ib = n.ibox ? [n.ibox[0] - n.x, n.ibox[1] - n.y, n.ibox[2], n.ibox[3]] : [0, 0, n.w, n.h];
      img.style.cssText = `position:absolute;left:${ib[0] * u}px;top:${ib[1] * u}px;width:${ib[2] * u}px;height:${ib[3] * u}px;display:block;`;
      el.appendChild(img);
    }
    if (n.text != null && n.font) {
      const f = n.font;
      const t = document.createElement('div');
      const tb = n.tbox ? { x: n.tbox[0] - n.x, y: n.tbox[1] - n.y, w: n.tbox[2], h: n.tbox[3] } : { x: 0, y: 0, w: n.w, h: n.h };
      const jc = f.align === 'left' ? 'flex-start' : f.align === 'right' ? 'flex-end' : 'center';
      const ai = f.valign === 'top' ? 'flex-start' : f.valign === 'bottom' ? 'flex-end' : 'center';
      t.style.cssText = `position:absolute;left:${tb.x * u}px;top:${tb.y * u}px;width:${tb.w * u}px;height:${tb.h * u}px;display:flex;justify-content:${jc};align-items:${ai};text-align:${f.align};` +
        `font-family:'${f.family}';font-size:${f.size * u}px;font-weight:${f.bold ? 700 : 400};color:${css(f.color)};white-space:${f.wrap ? 'normal' : 'nowrap'};` +
        (f.spacing ? `letter-spacing:${f.spacing * u}px;` : '') + (f.lineHeight ? `line-height:${f.lineHeight * u}px;` : 'line-height:normal;') + (f.upper ? 'text-transform:uppercase;' : '') +
        (f.shadow ? `text-shadow:${f.shadow.x * u}px ${f.shadow.y * u}px ${f.shadow.blur * u}px ${css(f.shadow.color)};` : '');
      const inner = document.createElement('div');
      inner.textContent = n.text;
      t.appendChild(inner);
      el.appendChild(t);
    }
    (parent ?? rootEl).appendChild(el);
    made.set(n.name, el);
  }
  if (solo && at && (at.dx || at.dy)) rootEl.style.transform = `translate(${at.dx}px, ${at.dy}px)`;
  frame.appendChild(rootEl);
  return rootEl;
}

/** the description alone, without pictures (looks stay numbers) */
export function exportUi(frame) {
  const ui = createUiExport(frame);
  const { doc } = ui.plan();
  ui.done();
  return doc;
}
