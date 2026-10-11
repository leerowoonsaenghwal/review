"""Re-make only some clips for every breed, without rebuilding the body (rebuild_cats.py takes hours a breed and
can lose clips that only just pass): export_cats.py with ONLY=<clips> -> blender_motion.py -> assets/cats/<id>_motion.fbx.
Unity (tools/sync_art.py, CatArtImport) uses these clips in place of the main file's clips with the same name.
   python3 motion_cats.py OUT_DIR Flop,FlopIdle,FlopUp [id,id,...] [--jobs 6]      (needs: python3 -m http.server 8766 here)
"""
import os, re, shutil, subprocess, sys, time
from concurrent.futures import ThreadPoolExecutor, as_completed

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.abspath(os.path.join(HERE, '..', '..', 'assets', 'cats'))
args = sys.argv[1:]
out, only = os.path.abspath(args[0]), args[1]
ids = args[2].split(',') if len(args) > 2 and not args[2].startswith('--') else re.findall(r"^  \{ id: '([a-z_]+)', ko:", open(os.path.join(HERE, 'catgen.js')).read(), re.M)[:33]
JOBS = int(args[args.index('--jobs') + 1]) if '--jobs' in args else 6
PY = sys.executable
os.makedirs(os.path.join(out, 'raw'), exist_ok=True)


def one(cid):
    t0 = time.time(); log = open(os.path.join(out, cid + '.log'), 'w')
    raw = os.path.join(out, 'raw', f'{cid}_raw.glb')
    if not os.path.exists(raw):
        r = subprocess.run([PY, 'export_cats.py', os.path.join(out, 'raw'), cid], cwd=HERE, stdout=log, stderr=subprocess.STDOUT, env={**os.environ, 'ONLY': only})
        if r.returncode or not os.path.exists(raw): return cid, 'EXPORT FAILED', time.time() - t0
    fbx = os.path.join(out, f'{cid}_motion.fbx')
    subprocess.run([PY, 'blender_motion.py', raw, fbx], cwd=HERE, stdout=log, stderr=subprocess.STDOUT)
    if not os.path.exists(fbx): return cid, 'BLENDER FAILED', time.time() - t0
    shutil.copy(fbx, os.path.join(ASSETS, f'{cid}_motion.fbx'))
    return cid, 'ok', time.time() - t0


with ThreadPoolExecutor(JOBS) as ex:
    for f in as_completed([ex.submit(one, c) for c in ids]):
        cid, res, sec = f.result()
        print(f'{cid:22s} {res}  ({sec / 60:.0f} min)', flush=True)
