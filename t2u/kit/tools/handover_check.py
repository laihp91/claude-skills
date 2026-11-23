"""Handover check: is a converted Unity project ready for a dev team to maintain, extend and debug?
usage: python3 handover_check.py <UnityProject> [--json]      exit 1 if any FAIL
Rules: ~/.claude/skills/t2u/reference/handover-standard.md (H1..H15). Static checks only; run tests separately."""
import json, re, sys
from pathlib import Path

args = [a for a in sys.argv[1:] if not a.startswith('--')]
root = Path(args[0] if args else '.').resolve()
game = root / 'Assets' / '_Game'
scripts = game / 'Scripts'
issues = []
def add(level, rule, msg, where=None): issues.append({'level': level, 'rule': rule, 'msg': msg, 'where': where})
def cs_files(d): return sorted(d.rglob('*.cs')) if d.exists() else []
def rel(p): return str(p.relative_to(root))
read = lambda p: p.read_text(encoding='utf-8', errors='ignore')

kit_settings = {}
ks = root / 'ProjectSettings' / 'T2UKit.json'
if ks.exists():
    try: kit_settings = json.loads(read(ks))
    except Exception: add('WARN', 'H0', 'ProjectSettings/T2UKit.json is not valid JSON')

# H1 tests self-contained (no paths outside the repo)
tests = cs_files(game / 'Tests') + cs_files(scripts)
outside = [rel(p) for p in tests if re.search(r'"\.\./|\.\.[\\/]+web|Path\.Combine\([^)]*"\.\."', read(p))]
if outside: add('FAIL', 'H1', 'Tests/scripts read files OUTSIDE the Unity repo (a dev who clones only this repo gets failing or empty tests). Copy fixtures into the repo (goldenDir).', outside[:6])

# H2 golden recordings + expected logs inside the repo
gdir = root / kit_settings.get('goldenDir', 'TestData/Golden')
inputs = list(gdir.glob('*.input.json')) if gdir.exists() else []
if not inputs: add('FAIL', 'H2', f'No golden recordings in {rel(gdir) if gdir.is_relative_to(root) else gdir} (copy unity-convert/recordings/*.input.json + .state.jsonl + .events.jsonl).')
else:
    miss = [p.name for p in inputs if not (gdir / p.name.replace('.input.json', '.state.jsonl')).exists()]
    if miss: add('FAIL', 'H2', 'Golden recordings without expected state logs', miss[:6])

# H3 replay tests cannot pass with zero cases
alltests = '\n'.join(read(p) for p in cs_files(game / 'Tests'))
if 'Recordings.Check' not in alltests and 'ReplayRunner' not in alltests and 'ReplayDriver' not in alltests:
    add('FAIL', 'H3', 'No replay (golden) regression test found.')
elif not re.search(r'HasRecordings|Greater(OrEqual)?\(\s*Names|Assert\.That\(\s*Names', alltests):
    add('WARN', 'H3', 'Replay tests use TestCaseSource but nothing asserts that recordings exist: with an empty folder they pass silently. Add a HasRecordings test.')

# H4 maintenance-mode CLAUDE.md
cm = root / 'CLAUDE.md'
if not cm.exists(): add('FAIL', 'H4', 'No CLAUDE.md for the team / AI assistants.')
else:
    t = read(cm)
    if 'MAINTENANCE' not in t: add('FAIL', 'H4', 'CLAUDE.md is still in conversion mode: replace it with the maintenance template (rules for changing gameplay, golden re-baseline, generated content).')
    if re.search(r'Do not "improve"|keep names, numbers and order of operations identical', t): add('WARN', 'H4', 'CLAUDE.md still forbids improving the logic (conversion-only rule).')

# H5 README sections
rd = root / 'README.md'
need = {'architecture': r'(?i)^#+ .*(architecture|layout|structure)', 'change gameplay': r'(?i)^#+ .*(chang|gameplay|add a level|extend)', 'golden tests': r'(?i)golden', 'build': r'(?i)^#+ .*build', 'debug': r'(?i)^#+ .*(debug|troubleshoot|known)'}
if not rd.exists(): add('FAIL', 'H5', 'No README.md')
else:
    t = read(rd)
    lack = [k for k, r in need.items() if not re.search(r, t, re.M)]
    if lack: add('WARN', 'H5', 'README misses sections: ' + ', '.join(lack))

# H6 generated content frozen
if not kit_settings.get('generatedFrozen', False):
    gens = [rel(p) for p in cs_files(game / 'Editor') if re.search(r'SaveAsPrefabAsset|EditorSceneManager\.SaveScene|SaveScene\(', read(p))]
    if gens: add('WARN', 'H6', 'Scene/prefab generators can still overwrite team edits: set "generatedFrozen": true in T2UKit.json and make generators respect it (create-if-missing only).', gens[:6])

# H7 git hygiene
ga = root / '.gitattributes'
if not ga.exists() or 'unityyamlmerge' not in read(ga): add('WARN', 'H7', 'No .gitattributes with UnityYAMLMerge (scene/prefab merges will conflict). Copy kit/project-files/gitattributes.')
if not (root / '.editorconfig').exists(): add('WARN', 'H7', 'No .editorconfig (code style). Copy kit/project-files/editorconfig.')

# H8 debug-only code guarded
def unguarded_lines(text, pattern):
    """lines matching pattern that are NOT inside #if UNITY_EDITOR / DEVELOPMENT_BUILD"""
    stack, out = [], []
    for i, line in enumerate(text.split('\n'), 1):
        st = line.strip()
        if st.startswith('#if'): stack.append(bool(re.search(r'UNITY_EDITOR|DEVELOPMENT_BUILD', st)) and '!' not in st)
        elif st.startswith('#elif') or st.startswith('#else'):
            if stack: stack[-1] = False
        elif st.startswith('#endif'):
            if stack: stack.pop()
        elif re.search(pattern, line.split('//')[0]) and not any(stack): out.append(i)
    return out

dbg = []
for p in cs_files(scripts):
    for ln in unguarded_lines(read(p), r'Key\.wasPressedThisFrame|GetKeyDown'): dbg.append(f'{rel(p)}:{ln}')
if dbg: add('WARN', 'H8', 'Debug keyboard shortcuts compiled into release builds (wrap in #if UNITY_EDITOR || DEVELOPMENT_BUILD).', dbg)

# H9 automation code separated from the bootstrap
auto = [rel(p) for p in cs_files(scripts) if 'GetCommandLineArgs' in read(p) and not re.search(r'(?i)automation|debug|devtools', p.name)]
if auto: add('WARN', 'H9', 'Test-automation code (command-line flags) mixed into game scripts: move it to an AutomationDriver component (editor/dev builds only).', auto)

# H10 player-facing texts not hard-coded
txt = []
for p in cs_files(scripts):
    for i, line in enumerate(read(p).split('\n'), 1):
        code = line.split('//')[0]
        if re.search(r'(Toast|\.text\s*=|SetText)\s*\(?\s*[^;]*"[A-Z][a-z]+ [a-z]', code): txt.append(f'{rel(p)}:{i}')
if txt: add('WARN', 'H10', f'{len(txt)} player-facing text(s) hard-coded in C#: use keys + TextTable (data/strings.json) so texts can be edited/translated.', txt[:6])

# H11 save data versioned
saves = [p for p in cs_files(scripts) if re.search(r'(?i)save', p.name)]
if saves and not any(re.search(r'\bVersion\b', read(p)) for p in saves): add('WARN', 'H11', 'Save data has no version field (future changes cannot migrate players\' progress).', [rel(p) for p in saves])

# H12 file size
big = [f'{rel(p)} ({len(read(p).splitlines())} lines)' for p in cs_files(scripts) if len(read(p).splitlines()) > 600]
if big: add('WARN', 'H12', 'Very large script files (> 600 lines): split by feature.', big)

# H13 dev-only tooling documented
man = root / 'Packages' / 'manifest.json'
if man.exists() and 'unity-mcp' in read(man) and not (rd.exists() and re.search(r'(?i)mcp', read(rd))):
    add('WARN', 'H13', 'AI/MCP tooling in the project but not explained in README (keep or remove before shipping).')

# H14 one MonoBehaviour per file
multi = [rel(p) for p in cs_files(scripts) if len(re.findall(r'class\s+\w+\s*:\s*MonoBehaviour', read(p))) > 1]
if multi: add('FAIL', 'H14', 'Several MonoBehaviours in one file (breaks prefab references).', multi)

# H15 art is files: no pictures made in game code (editor tools and dev-only code may)
made = []
for p in cs_files(scripts):
    if re.search(r'[\\/](Editor|Dev)[\\/]', rel(p)): continue
    for ln in unguarded_lines(read(p), r'new\s+Texture2D\s*\(|\.SetPixels?(32)?\s*\(|Sprite\.Create\s*\('): made.append(f'{rel(p)}:{ln}')
if made: add('FAIL', 'H15', 'Pictures created in game code: every look must be a PNG in Assets/_Game/Art that an artist can replace (producer rule).', made)
if rd.exists() and not re.search(r'(?i)^#+ .*(art|picture|sprite|asset)', read(rd), re.M):
    add('WARN', 'H15', 'README has no section saying how to replace a picture (art/sprites).')

fails = sum(1 for i in issues if i['level'] == 'FAIL')
warns = sum(1 for i in issues if i['level'] == 'WARN')
score = max(0, 100 - 15 * fails - 4 * warns)
if '--json' in sys.argv: print(json.dumps({'score': score, 'issues': issues}, indent=2)); sys.exit(1 if fails else 0)
for i in issues:
    print(f"{i['level']} {i['rule']}: {i['msg']}")
    for w in (i['where'] or [])[:6]: print(f'    {w}')
print(f'\nHandover score: {score}/100 ({fails} FAIL, {warns} WARN)')
print('Bàn giao: ĐẠT' if fails == 0 and score >= 85 else 'Bàn giao: CHƯA ĐẠT — sửa các mục FAIL trước')
sys.exit(1 if fails else 0)
