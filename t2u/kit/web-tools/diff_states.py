"""Logic parity report: web (JS) vs Unity state + event logs for every recording.
usage: python3 tools/t2u/diff_states.py [--uc unity-convert]   -> writes unity-convert/compare/logic_report.md; exit 1 if any diverges
Web logs:   recordings/<name>.state.jsonl / .events.jsonl
Unity logs: recordings/<name>.unity.state.jsonl / .unity.events.jsonl (written by the Unity replay parity test).
Numbers compared with tolerance.json (default exact), everything else exactly. Reports the FIRST differing frame + field."""
import json, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from ucdir import uc_dir

root = uc_dir()
rec = root / 'recordings'
tol_file = Path(__file__).parent / 'tolerance.json'
tol = json.loads(tol_file.read_text()) if tol_file.exists() else {'default': 0}

def lines(p):
    return [json.loads(l) for l in p.read_text().splitlines() if l.strip()] if p.exists() else None

def diff(a, b, path='', key=''):
    if isinstance(a, bool) or isinstance(b, bool):
        return None if a == b else (path, a, b)
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        return None if abs(a - b) <= tol.get(key, tol.get('default', 0)) else (path, a, b)
    if isinstance(a, dict) and isinstance(b, dict):
        for k in list(a.keys()) + [k for k in b.keys() if k not in a]:
            if k not in a or k not in b:
                return (f'{path}.{k}', a.get(k, '<missing>'), b.get(k, '<missing>'))
            d = diff(a[k], b[k], f'{path}.{k}', k)
            if d: return d
        return None
    if isinstance(a, list) and isinstance(b, list):
        if len(a) != len(b): return (f'{path} (length)', len(a), len(b))
        for i, (x, y) in enumerate(zip(a, b)):
            d = diff(x, y, f'{path}[{i}]', key)
            if d: return d
        return None
    return None if a == b else (path, a, b)

rows, failed = [], 0
for inp in sorted(rec.glob('*.input.json')):
    n = inp.name[:-len('.input.json')]
    out = {}
    for kind in ('state', 'events'):
        web, uni = lines(rec / f'{n}.{kind}.jsonl'), lines(rec / f'{n}.unity.{kind}.jsonl')
        if uni is None:
            out[kind] = ('MISSING', 'run the Unity replay parity test first'); continue
        first = None
        for i, (w, u) in enumerate(zip(web, uni)):
            d = diff(w, u)
            if d: first = (i, w.get('f'), d); break
        if first is None and len(web) != len(uni):
            first = (min(len(web), len(uni)), None, ('line count', len(web), len(uni)))
        out[kind] = ('OK', f'{len(web)} lines') if first is None else ('DIFF', f'line {first[0]} frame {first[1]}: {first[2][0]} web={first[2][1]} unity={first[2][2]}')
    ok = all(v[0] == 'OK' for v in out.values())
    failed += 0 if ok else 1
    rows.append((n, out, ok))

md = ['# Logic parity report (web vs Unity)', '', f'Recordings: {len(rows)} — passing: {len(rows) - failed} — failing: {failed}', '',
      '| Recording | State log | Event log | Result |', '|---|---|---|---|']
for n, out, ok in rows:
    md.append(f"| {n} | {out['state'][0]} ({out['state'][1]}) | {out['events'][0]} ({out['events'][1]}) | {'✅' if ok else '❌'} |")
(root / 'compare').mkdir(parents=True, exist_ok=True)
(root / 'compare' / 'logic_report.md').write_text('\n'.join(md) + '\n')
print('\n'.join(md))
sys.exit(1 if failed or not rows else 0)
