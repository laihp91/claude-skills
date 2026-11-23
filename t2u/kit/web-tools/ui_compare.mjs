// Compares the UI screens Unity drew with the ones the web drew, screen by screen.
//   node tools/t2u/ui_compare.mjs <unity pictures> [web pictures] [out]
// web pictures: unity-convert/logs/uicheck/web (written by `npm run t2u:uicheck`); Unity's: Tools ▸ T2U Kit ▸ Capture UI
// Screens (Captures/ui). Each pair: is any AREA different (a missing box, a wrong colour, something off by two design
// pixels), and how much of the picture is edge fringe (two renderers never share the last half pixel of an edge; text
// drawn by TextMesh Pro and by Chrome differs along every letter). Side-by-side pictures go to <out>.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { decodePng, encodePng, unlikePng, sideBySide } from './png.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const [unityDir, webDir = path.join(root, 'unity-convert/logs/uicheck/web'), out = path.join(root, 'unity-convert/logs/uicompare')] = process.argv.slice(2);
if (!unityDir) {
  console.log('usage: node tools/t2u/ui_compare.mjs <unity pictures> [web pictures] [out]');
  process.exit(2);
}
fs.mkdirSync(out, { recursive: true });
let bad = 0;
for (const f of fs.readdirSync(webDir).filter((x) => x.endsWith('.png')).sort()) {
  if (!fs.existsSync(path.join(unityDir, f))) {
    console.log(`MISSING ${f}`);
    bad++;
    continue;
  }
  const a = decodePng(fs.readFileSync(path.join(webDir, f)));
  const b = decodePng(fs.readFileSync(path.join(unityDir, f)));
  if (a.width !== b.width || a.height !== b.height) {
    console.log(`SIZE    ${f}: web ${a.width}x${a.height}, Unity ${b.width}x${b.height}`);
    bad++;
    continue;
  }
  const d = unlikePng(a, b, a.width / 1080);
  if (d.solid) bad++;
  console.log(`${d.solid ? 'DIFF' : 'OK  '} ${f.replace('.png', '').padEnd(10)} ${d.solid ? `areas differ (${d.solid} px)` : 'no area differs'}; fringe ${((d.n / (a.width * a.height)) * 100).toFixed(3)}%`);
  fs.writeFileSync(path.join(out, f), encodePng(sideBySide(a, b, d.mask)));
}
console.log(bad ? `${bad} screen(s) differ: pictures (web | Unity | difference) in ${path.relative(root, out)}` : 'all screens match');
process.exit(bad ? 1 : 0);
