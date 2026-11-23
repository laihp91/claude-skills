// "Unity-ready" checker: scans a Three.js game and reports what would make the Unity conversion slow or inexact.
//   node tools/t2u/check_ready.mjs [projectDir] [--core src/core] [--json]
// Exit code 1 when there are blocking problems (ERR). Rules: see UNITY_READY.md (/game-new skill).
import fs from 'node:fs';
import path from 'node:path';

const args = process.argv.slice(2);
const opt = (k, d) => (args.includes(k) ? args[args.indexOf(k) + 1] : d);
const root = path.resolve(args.find((a, i) => !a.startsWith('--') && !(i > 0 && args[i - 1].startsWith('--'))) || '.');
const coreRel = opt('--core', 'src/core');
const core = path.join(root, coreRel);
const issues = [];
const add = (level, rule, file, line, msg) => issues.push({ level, rule, file: file && path.relative(root, file), line, msg });

const walk = (d, out = []) => {
  if (!fs.existsSync(d)) return out;
  for (const e of fs.readdirSync(d, { withFileTypes: true })) {
    if (['node_modules', 'dist', '.git', 'unity-convert', '__t2u__'].includes(e.name)) continue;
    const p = path.join(d, e.name);
    if (e.isDirectory()) walk(p, out);
    else if (/\.(m?js|ts)$/.test(e.name)) out.push(p);
  }
  return out;
};

const hasCore = fs.existsSync(core);
if (!hasCore) add('ERR', 'core-folder', null, 0, `No pure-logic folder ${coreRel}/: game rules are mixed with rendering/UI (the converter must separate them by hand).`);
const coreFiles = hasCore ? walk(core) : walk(path.join(root, 'src'));
const isLib = (f) => /[\\/]core[\\/]lib[\\/]/.test(f);

// [level, rule, regex, message, appliesToLibToo]
const coreRules = [
  ['ERR', 'no-three', /from\s+['"]three['"]|from\s+['"]three\//, 'imports three.js (rendering belongs in src/view)'],
  ['ERR', 'no-dom', /\b(document|window|localStorage|sessionStorage|navigator|requestAnimationFrame|HTMLElement)\b/, 'uses the browser/DOM (belongs in src/ui or src/services)'],
  ['ERR', 'no-math-random', /Math\.random\s*\(/, 'Math.random: use the seeded rng from core/lib/rng.js', true],
  ['ERR', 'no-wall-clock', /\b(Date\.now|new Date|performance\.now)\b/, 'wall-clock time: logic must only use dt from update(dt)', true],
  ['ERR', 'no-timers', /\b(setTimeout|setInterval|requestIdleCallback)\s*\(/, 'browser timers: use clock.after / clock.every from core/lib/clock.js', true],
  ['ERR', 'no-async', /\basync\b|\bawait\b|\bnew Promise\b|\.then\s*\(|Promise\.(all|race|resolve)/, 'Promise/async: continuation order is hard to reproduce in C#; use clock callbacks', true],
  ['WARN', 'math-wrapper', /Math\.(sin|cos|tan|exp|pow|hypot|atan2?|log\d*|asin|acos|cbrt|sinh|cosh|tanh|expm1|log1p)\s*\(/, 'Math.* function that is not bit-exact in C#: use M.sin/cos/exp/log/hypot from core/lib/math.js; no Math.pow (write x*x*x, 2^x = M.exp(x*M.LN2), a fractional power = pow(b, e) from core/lib/math.js); no atan2/tan'],
  ['WARN', 'no-math-round', /Math\.round\s*\(/, 'Math.round differs from C# Math.Round: use round()/r3() from core/lib/math.js'],
  ['WARN', 'sort-comparator', /\.sort\(\s*\)/, 'sort() without comparator sorts as strings; always pass a comparator with a tie-breaker'],
  ['WARN', 'for-in', /\bfor\s*\(\s*(const|let|var)\s+\w+\s+in\b/, 'for...in order on objects is easy to get wrong in C#: iterate an array'],
  ['WARN', 'delete-key', /\bdelete\s+\w+(\.\w+)*\[/, 'dynamic delete on objects: prefer arrays/Maps with explicit order'],
];
for (const f of coreFiles) {
  const lines = fs.readFileSync(f, 'utf8').split('\n');
  lines.forEach((ln, i) => {
    const code = ln.replace(/\/\/.*$/, '');
    for (const [level, rule, re, msg, lib] of coreRules) {
      if (isLib(f) && !lib) continue;
      if (re.test(code)) add(level, rule, f, i + 1, msg);
    }
  });
}

// whole project checks
const exists = (p) => fs.existsSync(path.join(root, p));
if (!exists('data')) add('WARN', 'data-folder', null, 0, 'No data/ folder: levels and tuning numbers should be JSON (Unity imports the same files).');
if (!exists('src/t2u/harness.js') && !exists('src/__t2u__')) add('WARN', 'harness', null, 0, 'No record/replay harness (src/t2u/harness.js): Phase 1 of /t2u will have to add one.');
if (hasCore && !coreFiles.some((f) => /createRng|mulberry32/.test(fs.readFileSync(f, 'utf8')))) add('WARN', 'seeded-rng', null, 0, 'No seeded RNG found in the core.');
const pub = path.join(root, 'public');
const allAssets = [];
const walkA = (d) => fs.existsSync(d) && fs.readdirSync(d, { withFileTypes: true }).forEach((e) => (e.isDirectory() ? walkA(path.join(d, e.name)) : allAssets.push(path.join(d, e.name))));
walkA(pub);
walkA(path.join(root, 'src', 'assets'));
const webp = allAssets.filter((a) => /\.(webp|avif)$/i.test(a));
if (webp.length) add('WARN', 'png-sources', null, 0, `${webp.length} WebP/AVIF image(s): keep PNG masters too (Unity cannot import WebP).`);
const html = ['index.html', ...walk(path.join(root, 'src')).filter((f) => /[\\/]ui[\\/]/.test(f))].filter((p) => fs.existsSync(path.join(root, p)) || fs.existsSync(p));
const uiText = html.map((p) => fs.readFileSync(fs.existsSync(p) ? p : path.join(root, p), 'utf8')).join('\n');
if (!/data-ui-screen/.test(uiText)) add('WARN', 'ui-markup', null, 0, 'UI has no data-ui-screen / data-ui markers: Unity cannot rebuild screens automatically.');
const css = walk(path.join(root, 'src')).concat(fs.existsSync(path.join(root, 'src')) ? fs.readdirSync(path.join(root, 'src')).filter((f) => f.endsWith('.css')).map((f) => path.join(root, 'src', f)) : []);
const cssText = css.filter((f) => f.endsWith('.css')).map((f) => fs.readFileSync(f, 'utf8')).join('\n');
if (cssText && !/var\(--u\)/.test(cssText)) add('WARN', 'ui-units', null, 0, 'CSS does not size UI with var(--u) (1080-wide design units).');

const errs = issues.filter((i) => i.level === 'ERR').length;
const warns = issues.filter((i) => i.level === 'WARN').length;
const rulesWith = (lvl) => new Set(issues.filter((i) => i.level === lvl).map((i) => i.rule)).size;
const score = Math.max(0, 100 - rulesWith('ERR') * 15 - rulesWith('WARN') * 4);
if (args.includes('--json')) console.log(JSON.stringify({ score, errors: errs, warnings: warns, issues }, null, 2));
else {
  const byRule = {};
  for (const i of issues) (byRule[i.rule] ??= []).push(i);
  for (const [rule, list] of Object.entries(byRule)) {
    console.log(`${list[0].level} ${rule}: ${list[0].msg} (${list.length})`);
    for (const i of list.slice(0, 8)) if (i.file) console.log(`    ${i.file}:${i.line}`);
    if (list.length > 8) console.log(`    ... ${list.length - 8} more`);
  }
  console.log(`\nUnity-ready score: ${score}/100  (${errs} errors, ${warns} warnings)`);
  console.log(score >= 90 ? 'Điểm sẵn sàng chuyển Unity: TỐT' : score >= 60 ? 'Điểm sẵn sàng chuyển Unity: TẠM ĐƯỢC — nên sửa các lỗi ERR' : 'Điểm sẵn sàng chuyển Unity: KÉM — chuyển sẽ tốn nhiều công');
  // development tools that must not go out with the game. This is a reminder, not a fault: it costs no points.
  try {
    const dev = JSON.parse(fs.readFileSync('data/config.json', 'utf8')).dev ?? {};
    const on = Object.keys(dev).filter((k) => k !== '_' && dev[k] === true);
    if (on.length) console.log(`\nNHẮC: công cụ làm game đang BẬT — ${on.join(', ')} (data/config.json -> dev). Tắt trước khi phát hành.`);
  } catch {
    /* no config: nothing to remind about */
  }
}
process.exitCode = errs ? 1 : 0;
