"""Builds unity-convert/compare/visual.html: web | Unity | difference for every reference shot (export/shots.json),
with the mean difference (0 = identical, < 10 = nearly identical). Captions = the shot's `desc` (Vietnamese, for the producer).
usage: python3 tools/t2u/make_visual.py [--uc unity-convert]   (needs Pillow + numpy; reference/<id>.png and compare/unity/<id>.png)"""
import json, html, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageChops
sys.path.insert(0, str(Path(__file__).resolve().parent))
from ucdir import uc_dir

root = uc_dir()
shots = json.loads((root / 'export' / 'shots.json').read_text())['shots']
(root / 'compare').mkdir(parents=True, exist_ok=True)
rows = []
for s in shots:
    sid = s['id']
    w, u = root / 'reference' / f'{sid}.png', root / 'compare' / 'unity' / f'{sid}.png'
    if not (w.exists() and u.exists()):
        rows.append((s, None)); continue
    a, b = Image.open(w).convert('RGB'), Image.open(u).convert('RGB')
    if a.size != b.size: b = b.resize(a.size)
    d = ImageChops.difference(a, b)
    d.point(lambda v: min(255, v * 3)).save(root / 'compare' / f'{sid}_diff.png')
    rows.append((s, float(np.asarray(d).astype(float).mean())))
out = ['<!doctype html><meta charset="utf-8"><title>Web vs Unity</title>',
       '<style>body{font:15px system-ui;margin:20px;background:#f4f6fa}h2{margin:28px 0 6px}.row{display:flex;gap:10px}figure{margin:0}img{width:300px;border:1px solid #ccc;background:#fff}figcaption{font-size:13px;color:#555}.m{font-weight:bold}</style>',
       '<h1>So sánh hình: bản web (trái) — Unity (giữa) — chỗ khác nhau (phải, càng sáng càng khác)</h1>',
       '<p>Điểm chênh lệch trung bình: 0 = giống hệt, dưới 10 = gần như giống.</p>']
for s, m in rows:
    sid, cap = s['id'], html.escape(s.get('desc', s['id']))
    if m is None:
        out.append(f'<h2>{cap} <small>({sid})</small></h2><p>Chưa có ảnh Unity.</p>'); continue
    out.append(f'<h2>{cap} <small>({sid})</small> — chênh lệch <span class="m">{m:.1f}</span>/255</h2><div class="row">'
               f'<figure><img src="../reference/{sid}.png"><figcaption>Web</figcaption></figure>'
               f'<figure><img src="unity/{sid}.png"><figcaption>Unity</figcaption></figure>'
               f'<figure><img src="{sid}_diff.png"><figcaption>Khác nhau</figcaption></figure></div>')
(root / 'compare' / 'visual.html').write_text('\n'.join(out))
for s, m in rows: print(f"{s['id']:28s} {'-' if m is None else f'{m:.2f}'}")
