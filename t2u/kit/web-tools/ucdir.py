"""Finds the unity-convert folder: --uc <dir>, else <project>/unity-convert when the tools live in <project>/tools/t2u,
else the parent folder when they live in unity-convert/tools (older layout)."""
import sys
from pathlib import Path

def uc_dir():
    if '--uc' in sys.argv:
        return Path(sys.argv[sys.argv.index('--uc') + 1]).resolve()
    here = Path(__file__).resolve().parent
    cand = here.parents[1] / 'unity-convert'
    return cand if cand.exists() or here.parent.name != 'unity-convert' else here.parent
