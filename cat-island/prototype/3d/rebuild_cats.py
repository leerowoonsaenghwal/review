"""Rebuild breeds end to end and put them in assets/cats/: export_cats.py (sculpt + clips, 30 min - 2 h each)
-> blender_finish.py (game mesh, per-texel coat) -> cat_clips_json.py. Breeds run in parallel, one per core.
   python3 rebuild_cats.py OUT_DIR [id,id,...] [--jobs 6]      (needs: python3 -m http.server 8766 here)
Prints one line per finished breed with the clips it had to leave out (compare with docs/DEV_LOG.md).
"""
import json, os, re, shutil, subprocess, sys, time
from concurrent.futures import ThreadPoolExecutor, as_completed

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.abspath(os.path.join(HERE, '..', '..', 'assets', 'cats'))
args = sys.argv[1:]
out = os.path.abspath(args[0])
ids = args[1].split(',') if len(args) > 1 and not args[1].startswith('--') else re.findall(r"^  \{ id: '([a-z_]+)', ko:", open(os.path.join(HERE, 'catgen.js')).read(), re.M)[:33]
JOBS = int(args[args.index('--jobs') + 1]) if '--jobs' in args else 6
PY = sys.executable
PHOTO_COATS = 'tuxedo,cow,calico,tortie,cheese_white'
os.makedirs(os.path.join(out, 'raw'), exist_ok=True); os.makedirs(os.path.join(out, 'final'), exist_ok=True)


def one(cid):
    t0 = time.time(); log = open(os.path.join(out, cid + '.log'), 'w')
    raw = os.path.join(out, 'raw', f'{cid}_raw.glb')
    r = subprocess.run([PY, 'export_cats.py', os.path.join(out, 'raw'), cid], cwd=HERE, stdout=log, stderr=subprocess.STDOUT)
    if r.returncode or not os.path.exists(raw): return cid, 'EXPORT FAILED', time.time() - t0
    fin = os.path.join(out, 'final', cid)
    extra = ['--extra-coats', PHOTO_COATS] if cid == 'korean_shorthair' else []   # (photo cats whose pattern no breed has: unity CatCoat)
    r = subprocess.run([PY, 'blender_finish.py', raw, fin, cid] + extra, cwd=HERE, stdout=log, stderr=subprocess.STDOUT)
    if not os.path.exists(os.path.join(fin, cid + '.fbx')): return cid, 'BLENDER FAILED', time.time() - t0
    r = subprocess.run([PY, 'cat_clips_json.py', os.path.join(out, 'raw', cid + '.json'), fin], cwd=HERE, stdout=log, stderr=subprocess.STDOUT)
    for ext in ('.fbx', '.glb', '.clips.json', '_coatmask.png', '.coat.json'):
        src = os.path.join(fin, cid + ext)
        if os.path.exists(src): shutil.copy(src, os.path.join(ASSETS, cid + ext))
    for f in os.listdir(fin):
        if f.startswith(cid + '__'): shutil.copy(os.path.join(fin, f), os.path.join(ASSETS, f))
    info = json.load(open(os.path.join(out, 'raw', cid + '.json')))
    skipped = [s['clip'] for s in info.get('skippedClips', [])]
    return cid, 'ok skipped=' + ','.join(skipped), time.time() - t0


with ThreadPoolExecutor(JOBS) as ex:
    for f in as_completed([ex.submit(one, c) for c in ids]):
        cid, res, sec = f.result()
        print(f'{cid:22s} {res}  ({sec / 60:.0f} min)', flush=True)
