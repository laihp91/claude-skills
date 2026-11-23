// T2U bot for Unity-ready games (template from /game-new). Drives the game in headless Chrome through window.__t2u.
//   node tools/t2u/bot.mjs record [names...]   run bot scenarios (src/t2u/scenarios.js) -> unity-convert/recordings/
//   node tools/t2u/bot.mjs verify [names...]   replay every recording twice; must give identical logs (determinism)
//   node tools/t2u/bot.mjs export              data/*.json, UI layouts (export/ui.json), twin-library goldens, game extras
//   node tools/t2u/bot.mjs shots               reference screenshots listed in scenarios.js `shots`
//   node tools/t2u/bot.mjs uicheck             draws export/ui.json back by the importer's rules and compares every
//                                              screen and every state with the real UI, pixel by pixel
// Output folder: <project>/unity-convert/. Chrome: env CHROME or the installed Google Chrome.
import { chromium } from 'playwright-core';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { decodePng, encodePng, diffPng, unlikePng, sideBySide, COMPARE } from './png.mjs';
import { createSpriteSet } from './ui_bake.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(process.env.T2U_ROOT || path.join(here, '../..'));
const UC = path.join(root, 'unity-convert');
const REC = path.join(UC, 'recordings');
const EXP = path.join(UC, 'export');
const REF = path.join(UC, 'reference');
const PORT = parseInt(process.env.T2U_PORT || '5188', 10);
const BASE = `http://127.0.0.1:${PORT}`;
const CHROME = process.env.CHROME || ['/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', '/usr/bin/google-chrome', 'C:/Program Files/Google/Chrome/Application/chrome.exe'].find((p) => fs.existsSync(p));
const VIEWPORT = { width: 540, height: 960 };
// the UI is measured and photographed on a 1080-wide frame (1 CSS pixel = 1 design unit) at 2 device pixels per unit
const UI_VIEWPORT = { width: 1080, height: 1920 };
const UI_SCALE = 2;
const sha = (s) => crypto.createHash('sha256').update(s).digest('hex').slice(0, 16);
const wr = (p, s) => (fs.mkdirSync(path.dirname(p), { recursive: true }), fs.writeFileSync(p, typeof s === 'string' || Buffer.isBuffer(s) ? s : JSON.stringify(s, null, 2)));
const md = (d) => (fs.mkdirSync(d, { recursive: true }), d);
const jsonl = (arr) => arr.map((x) => JSON.stringify(x)).join('\n') + '\n';

let server;
async function startServer() {
  // a run that has just ended may still be closing its server on this port: talking to a dying server hangs the
  // next run (seen with `uicheck` followed at once by `verify`). Wait for the port to fall silent first.
  for (let i = 0; i < 40; i++) {
    try {
      await fetch(BASE);
    } catch {
      break;
    }
    await new Promise((r) => setTimeout(r, 250));
  }
  const vite = path.join(root, 'node_modules/vite/bin/vite.js');
  server = spawn(process.execPath, [vite, '--port', String(PORT), '--strictPort', '--host', '127.0.0.1'], { cwd: root, stdio: 'ignore' });
  for (let i = 0; i < 80; i++) {
    try {
      if ((await fetch(BASE)).ok) return;
    } catch {}
    await new Promise((r) => setTimeout(r, 250));
  }
  throw new Error('dev server did not start (npm install done?)');
}

let browser;
async function newPage(viewport = VIEWPORT, deviceScaleFactor = 1) {
  browser ??= await chromium.launch({ executablePath: CHROME, headless: true, args: ['--ignore-gpu-blocklist', '--enable-unsafe-swiftshader', '--use-angle=swiftshader', '--disable-lcd-text'] });
  const ctx = await browser.newContext({ viewport, deviceScaleFactor });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => console.log('  [pageerror]', e.message));
  page.on('console', (m) => m.type() === 'error' && console.log('  [console.error]', m.text().slice(0, 200)));
  await page.goto(`${BASE}/?t2u=1`);
  await page.waitForFunction(() => window.__t2u && window.__t2u.game, null, { timeout: 60000 });
  return page;
}

function save(name, r) {
  const input = { name, seed: r.seed, dt: r.dt, logEvery: r.logEvery, totalFrames: r.totalFrames, events: r.events };
  wr(path.join(REC, `${name}.input.json`), JSON.stringify(input));
  wr(path.join(REC, `${name}.state.jsonl`), jsonl(r.states));
  wr(path.join(REC, `${name}.events.jsonl`), jsonl(r.log));
  const last = r.states[r.states.length - 1];
  const summary = { name, seed: r.seed, frames: r.totalFrames, inputs: r.events.length, snapshots: r.states.length, events: r.log.length, final: last, stateHash: sha(jsonl(r.states)), eventsHash: sha(jsonl(r.log)) };
  wr(path.join(REC, `${name}.summary.json`), summary);
  return summary;
}

async function record(names) {
  const page = await newPage();
  const list = names.length ? names : await page.evaluate(() => window.__t2u.scenarioNames());
  await page.context().close();
  for (const n of list) {
    const p = await newPage();
    const t0 = Date.now();
    const r = await p.evaluate((name) => window.__t2u.runScenario(name), n);
    const s = save(n, r);
    const f = s.final;
    console.log(`${n}: ${s.frames} frames, ${s.inputs} inputs, ${s.events} events, final state=${f.state} ui=${f.ui} level=${f.level}  (${((Date.now() - t0) / 1000).toFixed(1)}s)`);
    await p.context().close();
  }
}

async function replay(input) {
  const p = await newPage();
  const r = await p.evaluate((inp) => window.__t2u.replay(inp), input);
  await p.context().close();
  return { stateHash: sha(jsonl(r.states)), eventsHash: sha(jsonl(r.log)) };
}

async function verify(names) {
  const all = fs.readdirSync(REC).filter((f) => f.endsWith('.input.json')).map((f) => f.replace('.input.json', ''));
  let bad = 0;
  for (const n of names.length ? names : all) {
    const input = JSON.parse(fs.readFileSync(path.join(REC, `${n}.input.json`), 'utf8'));
    const sum = JSON.parse(fs.readFileSync(path.join(REC, `${n}.summary.json`), 'utf8'));
    const a = await replay(input);
    const b = await replay(input);
    const ok = [a, b].every((x) => x.stateHash === sum.stateHash && x.eventsHash === sum.eventsHash);
    bad += ok ? 0 : 1;
    console.log(`${ok ? 'OK  ' : 'FAIL'} ${n}`);
  }
  console.log(bad ? `NOT deterministic: ${bad} recording(s)` : 'ALL deterministic');
  return bad === 0;
}

async function doExport() {
  // 1) data files as they are
  const dataDir = path.join(root, 'data');
  for (const f of fs.existsSync(dataDir) ? fs.readdirSync(dataDir) : []) if (f.endsWith('.json')) fs.copyFileSync(path.join(dataDir, f), path.join(md(path.join(EXP, 'data')), f));
  console.log(`data: ${fs.readdirSync(md(path.join(EXP, 'data'))).join(', ')}`);
  // 2) the UI: layout, texts, states (export/ui.json) and the look of every element as sprites (export/ui/*.png)
  await exportUi();
  // 3) goldens of the twin libraries, computed by Chrome (the same engine that made the recordings)
  const p = await newPage();
  const goldens = await p.evaluate(async () => {
    const { createRng } = await import('/src/core/lib/rng.js');
    const { createClock } = await import('/src/core/lib/clock.js');
    const { ease } = await import('/src/core/lib/ease.js');
    const f64 = new Float64Array(1);
    const u64 = new BigUint64Array(f64.buffer);
    const hx = (v) => ((f64[0] = v), u64[0].toString(16).padStart(16, '0'));
    const mathLib = await import('/src/core/lib/math.js');
    const out = { ua: navigator.userAgent, rng: {}, ease: {}, math: { sin: [], cos: [], exp: [], hypot: [], log: [], pow: [] }, clock: [] };
    for (const seed of [1, 42, 123456789, 4294967295]) {
      const r = createRng(seed);
      out.rng[seed] = { next: Array.from({ length: 64 }, () => hx(r.next())), int: Array.from({ length: 32 }, () => r.int(-5, 17)), shuffle: r.shuffle([0, 1, 2, 3, 4, 5, 6, 7, 8, 9]) };
    }
    const r = createRng(99);
    const ts = [0, 1, 0.5, ...Array.from({ length: 200 }, () => r.next())];
    for (const [k, f] of Object.entries(ease)) out.ease[k] = ts.map((t) => [hx(t), hx(f(t))]);
    for (let i = 0; i < 3000; i++) {
      const x = r.next() * 200 - 100;
      out.math.sin.push([hx(x), hx(Math.sin(x))]);
      out.math.cos.push([hx(x), hx(Math.cos(x))]);
      const e = r.next() * 40 - 30;
      out.math.exp.push([hx(e), hx(Math.exp(e))]);
      const a = r.next() * 40 - 20, b = r.next() * 40 - 20;
      out.math.hypot.push([hx(a), hx(b), hx(Math.hypot(a, b))]);
      // log: values near 1 (ratios, growth factors), whole numbers (counts, costs) and the full range 1e-17..1e17
      const l = i % 3 === 0 ? r.next() * 3 : i % 3 === 1 ? Math.floor(r.next() * 1e6) + 1 : Math.exp(r.next() * 80 - 40);
      out.math.log.push([hx(l), hx(Math.log(l))]);
      // pow(b, e) of core/lib/math.js (e^(e ln b)): the game's own helper, when it has one
      if (mathLib.pow) {
        const pb = i % 2 ? 1 + r.next() * 3 : r.next() * 1e6;
        const pe = r.next() * 6 - 3;
        out.math.pow.push([hx(pb), hx(pe), hx(mathLib.pow(pb, pe))]);
      }
    }
    // clock trace: a fixed script of timers/tweens/cancels; records every callback in order
    const c = createClock();
    const log = [];
    let id2 = 0;
    c.after(0.05, () => log.push(['a1', +c.time.toFixed(6)]));
    const ev = c.every(0.04, () => log.push(['e1', +c.time.toFixed(6)]));
    c.tween(0.1, (k, raw) => log.push(['tw', hx(k), hx(raw)]), ease.outBack, () => {
      log.push(['tw.done', +c.time.toFixed(6)]);
      c.after(0, () => log.push(['a0-from-done', +c.time.toFixed(6)]));
    });
    id2 = c.after(0.2, () => log.push(['never']));
    c.after(0.07, () => {
      log.push(['cancel', c.count]);
      c.cancel(id2);
      c.cancel(ev);
      c.after(0.01, () => log.push(['nested', +c.time.toFixed(6)]));
    });
    for (let i = 0; i < 20; i++) {
      c.update(1 / 60);
      log.push(['frame', i, c.count]);
    }
    c.clear();
    c.update(1 / 60);
    log.push(['cleared', c.count]);
    out.clock = log;
    return out;
  });
  wr(path.join(EXP, 'goldens.json'), JSON.stringify(goldens));
  console.log(`goldens.json (${goldens.ua.match(/Chrome\/[\d.]+/)?.[0]})`);
  // 4) what the game itself bakes for Unity (window.__t2u.extras, see harness.js): a data URL → that file, else JSON
  const extras = await p.evaluate(() => Object.keys(window.__t2u.extras ?? {}));
  let bytes = 0;
  for (const name of extras) {
    const v = await p.evaluate((n) => window.__t2u.extras[n](), name);
    const m = typeof v === 'string' && v.match(/^data:[^;,]*;base64,(.*)$/s);
    const body = m ? Buffer.from(m[1], 'base64') : JSON.stringify(v, null, 2);
    wr(path.join(EXP, name), body);
    bytes += body.length;
  }
  if (extras.length) console.log(`extras: ${extras.length} files, ${(bytes / 1048576).toFixed(1)} MB (${[...new Set(extras.map((n) => n.split('/')[0]))].join(', ')})`);
  await p.context().close();
}

/** a clip inside the page, on whole pixels; says which sides the page edge cut */
function pageClip(c, view) {
  const x0 = Math.max(0, Math.floor(c.x)), y0 = Math.max(0, Math.floor(c.y));
  const x1 = Math.min(view.width, Math.ceil(c.x + c.width)), y1 = Math.min(view.height, Math.ceil(c.y + c.height));
  return { clip: { x: x0, y: y0, width: Math.max(1, x1 - x0), height: Math.max(1, y1 - y0) }, edges: { left: c.x < 0, top: c.y < 0, right: c.x + c.width > view.width, bottom: c.y + c.height > view.height } };
}

async function exportUi() {
  const p = await newPage(UI_VIEWPORT, UI_SCALE);
  const { doc, looks } = await p.evaluate(() => window.__t2u.uiExport().plan());
  // photograph every distinct look (the element alone, without its text, on a transparent page)
  // (the sprites of the last export: a picture that has not changed keeps its name and its file)
  const previous = {};
  try {
    const old = JSON.parse(fs.readFileSync(path.join(EXP, 'ui.json'), 'utf8'));
    for (const [name, sp] of Object.entries(old.sprites ?? {})) previous[name] = { border: sp.border, img: decodePng(fs.readFileSync(path.join(EXP, 'ui', `${name}.png`))) };
  } catch {
    /* first export */
  }
  const set = createSpriteSet(UI_SCALE, previous);
  const result = [];
  for (let i = 0; i < looks.length; i++) {
    const s = await p.evaluate((k) => window.__t2u.ui.shoot(k), i);
    const { clip, edges } = pageClip(s.clip, UI_VIEWPORT);
    const photo = decodePng(await p.screenshot({ clip, omitBackground: true }));
    await p.evaluate(() => window.__t2u.ui.unshoot());
    if (looks[i].plainFile) {
      // a picture file under a clipping box: is it really cut? The same photograph without the clipping tells
      const s2 = await p.evaluate((k) => window.__t2u.ui.shoot(k, true), i);
      const free = decodePng(await p.screenshot({ clip, omitBackground: true }));
      await p.evaluate(() => window.__t2u.ui.unshoot());
      const still = ['x', 'y', 'width', 'height'].every((k) => Math.abs(s.box[k] - s2.box[k]) < 0.01);
      if (still && diffPng(photo, free).pixels === 0) {
        result.push({ image: looks[i].plainFile });
        continue;
      }
    }
    result.push(set.add(photo, clip, s.box, s.u, looks[i].hint, edges, !looks[i].picture));
  }
  await p.evaluate(() => window.__t2u.ui.done());
  await p.context().close();
  // looks → skins
  const skin = (n) => {
    if (!('look' in n)) return;
    const r = n.look == null ? null : result[n.look];
    delete n.look;
    if (r && r.image) {
      // not a skin after all: the picture file itself
      n.skin = null;
      n.image = r.image.split('/').pop();
      doc.images[n.image] = r.image;
      return;
    }
    n.skin = r ? r.skin : null;
    if (r && r.skinBox.some((v) => Math.abs(v) > 0.005)) n.skinBox = r.skinBox;
    else delete n.skinBox;
  };
  for (const s of doc.screens) s.nodes.forEach(skin);
  for (const st of doc.states) Object.values(st.set).forEach(skin);
  // an element that rests as a picture file and wears a skin in some state shows the skin INSTEAD of the file there
  const files = new Set(doc.screens.flatMap((s) => s.nodes.filter((n) => n.image && !n.skin).map((n) => n.name)));
  for (const st of doc.states) for (const [name, d] of Object.entries(st.set)) if (files.has(name) && d.skin && !('image' in d)) d.image = null;
  doc.scale = UI_SCALE;
  doc.sprites = {};
  const dir = path.join(EXP, 'ui');
  fs.rmSync(dir, { recursive: true, force: true });
  let bytes = 0;
  for (const [name, sp] of Object.entries(set.sprites)) {
    const png = encodePng(sp.img);
    wr(path.join(dir, `${name}.png`), png);
    bytes += png.length;
    doc.sprites[name] = { w: sp.w, h: sp.h, border: sp.border };
  }
  const lint = [...doc.lint, ...set.warnings];
  delete doc.lint;
  wr(path.join(EXP, 'ui.json'), doc);
  console.log(`ui.json: ${doc.screens.map((s) => `${s.screen}(${s.nodes.length})`).join(' ')}`);
  console.log(`ui skins: ${looks.length} looks photographed -> ${Object.keys(doc.sprites).length} sprites (${(bytes / 1024).toFixed(0)} KB), ${doc.states.length} state entries`);
  for (const m of lint) console.log(`  [ui] ${m}`);
  return doc;
}

/** export/ui.json drawn back by the importer's rules, compared with the real UI: every screen at rest, every state */
async function uiCheck(only) {
  const doc = JSON.parse(fs.readFileSync(path.join(EXP, 'ui.json'), 'utf8'));
  const p = await newPage(UI_VIEWPORT, UI_SCALE);
  const LOG = md(path.join(UC, 'logs', 'uicheck'));
  // both pictures on black, without the battlefield
  await p.addStyleTag({ content: 'html, body { background: #000 !important; } #game, canvas { visibility: hidden !important; } .t2u-off { display: none !important; }' });
  await p.evaluate(() => window.__t2u.uiExport());
  const sprite = '/unity-convert/export/ui/';
  const { TOL, SLIP, SOLID } = COMPARE;
  const unlike = (a, b) => unlikePng(a, b, UI_SCALE);
  const shootPair = async (screen, state, clip) => {
    // a state is checked on its element alone (showing a hidden row moves its neighbours: that is layout, not look)
    const box = await p.evaluate(([scr, st]) => window.__t2u.ui.pose(scr, st?.host, st?.on, !!st), [screen, state]);
    if (box && (box.y + box.height > UI_VIEWPORT.height || box.y < 0 || box.x < 0 || box.x + box.width > UI_VIEWPORT.width || box.width < 1 || box.height < 1)) {
      await p.evaluate(() => window.__t2u.ui.unshoot());
      return null;
    }
    const c = clip ?? (box ? pageClip({ x: box.x - 60, y: box.y - 60, width: box.width + 120, height: box.height + 120 }, UI_VIEWPORT).clip : undefined);
    const a = decodePng(await p.screenshot(c ? { clip: c } : {}));
    await p.evaluate(() => window.__t2u.ui.unshoot());
    await p.evaluate(([d, scr, base, st, at]) => {
      document.querySelectorAll('[data-ui-screen]').forEach((s) => s.classList.add('t2u-off'));
      window.__t2uPrev = window.__t2u.uiPreview(d, scr, (n) => `${base}${n}.png`, st, !!st, at);
      return Promise.all([...window.__t2uPrev.querySelectorAll('img')].map((i) => i.decode().catch(() => {})));
    }, [doc, screen, sprite, state, box]);
    await p.waitForFunction(() => document.fonts.status === 'loaded');
    await p.waitForTimeout(60); // border-image pictures are decoded off the main thread
    const b = decodePng(await p.screenshot(c ? { clip: c } : {}));
    await p.evaluate(() => {
      window.__t2uPrev.remove();
      document.querySelectorAll('.t2u-off').forEach((s) => s.classList.remove('t2u-off'));
    });
    const d = unlike(a, b);
    return { a, b, over: d.solid, fringe: d.n, mask: d.mask, any: diffPng(a, b) };
  };
  const side = sideBySide;
  let bad = 0;
  let worst = 0;
  let seen = 0;
  for (const s of doc.screens) {
    if (only.length && !only.includes(s.screen)) continue;
    seen++;
    const r = await shootPair(s.screen, null, null);
    const share = (r.over / (r.a.width * r.a.height)) * 100;
    worst = Math.max(worst, share);
    if (r.over) bad++;
    console.log(`${r.over ? 'DIFF' : 'OK  '} ${s.screen.padEnd(10)} ${r.over ? `an area differs (${r.over} px)` : 'no area differs'}; edge fringe: ${r.fringe} px of ${r.a.width * r.a.height} (${((r.fringe / (r.a.width * r.a.height)) * 100).toFixed(4)}%)`);
    if (r.over) wr(path.join(LOG, `${s.screen}.png`), encodePng(side(r.a, r.b, r.mask)));
    wr(path.join(LOG, 'web', `${s.screen}.png`), encodePng(r.a)); // the real screen alone on black: what Unity's capture is compared with
  }
  let stBad = 0;
  let n = 0;
  let unseen = 0;
  for (const st of doc.states) {
    const scr = doc.screens.find((s) => s.nodes.some((x) => x.name === st.host));
    if (only.length && !only.includes(scr.screen)) continue;
    const r = await shootPair(scr.screen, st, undefined);
    if (!r) {
      unseen++;
      continue;
    }
    n++;
    if (r.over) {
      stBad++;
      const file = `${scr.screen}__${st.host}__${st.on.join('+')}.png`.replace(/[^\w.+-]+/g, '_');
      wr(path.join(LOG, file), encodePng(side(r.a, r.b, r.mask)));
      if (stBad <= 40) console.log(`DIFF state ${st.host} [${st.on.join(' ')}]: an area differs (${r.over} px)`);
    }
  }
  console.log(`states: ${n - stBad} of ${n} match${unseen ? ` (${unseen} could not be photographed: outside the frame)` : ''}`);
  console.log(bad || stBad ? `UI CHECK: ${bad} screen(s) and ${stBad} state(s) differ — pictures (real | drawn back | difference) in unity-convert/logs/uicheck/` : `UI CHECK: all ${seen} screens and ${n} states match (no area of ${SOLID}x${SOLID} device pixels differs; edges may sit up to ${SLIP / UI_SCALE} design pixels off, colours ${TOL}/255)`);
  await p.context().close();
  return !(bad || stBad);
}

async function shots() {
  const { shots: list } = await (async () => {
    const p = await newPage();
    const r = await p.evaluate(async () => (await import('/src/t2u/scenarios.js')).shots.map((s) => ({ ...s })));
    await p.context().close();
    return { shots: r };
  })();
  const meta = [];
  for (const s of list) {
    const rec = JSON.parse(fs.readFileSync(path.join(REC, `${s.rec}.input.json`), 'utf8'));
    let F = s.frame;
    if (F == null) {
      const states = fs.readFileSync(path.join(REC, `${s.rec}.state.jsonl`), 'utf8').trim().split('\n').map((l) => JSON.parse(l));
      const hit = states.find((st) => st.ui === s.ui);
      if (!hit) { console.log(`skip ${s.id}: ui=${s.ui} never shown in ${s.rec}`); continue; }
      F = hit.f + (s.plus ?? 0);
    }
    const prefix = { ...rec, totalFrames: F, events: rec.events.filter((e) => e.f < F) };
    const p = await newPage();
    await p.evaluate((inp) => (window.__t2u.replay(inp), window.__t2u.render()), prefix);
    await p.waitForTimeout(100);
    await p.screenshot({ path: path.join(md(REF), `${s.id}.png`) });
    wr(path.join(REF, `${s.id}.input.json`), JSON.stringify(prefix));
    meta.push({ id: s.id, rec: s.rec, frame: F, desc: s.desc ?? s.id, viewport: VIEWPORT, replay: `reference/${s.id}.input.json` });
    console.log(`shot ${s.id} (${s.rec} @ ${F})`);
    await p.context().close();
  }
  wr(path.join(EXP, 'shots.json'), { shots: meta });
}

const [cmd, ...rest] = process.argv.slice(2);
if (!['record', 'verify', 'export', 'shots', 'uicheck'].includes(cmd)) {
  console.log('usage: node tools/t2u/bot.mjs record|verify|export|shots|uicheck [names]');
  process.exit(2);
}
await startServer();
try {
  if (cmd === 'record') await record(rest);
  else if (cmd === 'verify') process.exitCode = (await verify(rest)) ? 0 : 1;
  else if (cmd === 'export') await doExport();
  else if (cmd === 'shots') await shots();
  else if (cmd === 'uicheck') process.exitCode = (await uiCheck(rest)) ? 0 : 1;
} finally {
  await browser?.close();
  server?.kill();
}
