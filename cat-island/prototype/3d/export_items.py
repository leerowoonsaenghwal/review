"""Build every item as a game asset: items.js (dense raw) -> blender_items.py (remesh, decimate, bake) -> assets.
   python3 export_items.py OUT_DIR [id,id,...]
Per item: triangle budget, texture size and voxel size (small toys get fine voxels, furniture coarse ones).
"""
import json, os, shutil, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
out = os.path.abspath(sys.argv[1]); ids = sys.argv[2].split(',') if len(sys.argv) > 2 else None
SPEC = {   # id: (tris, texture, voxel m)
    'food_bowl': (3000, 1024, .0015), 'water_bowl': (2400, 512, .0015), 'milk_bowl': (2400, 512, .0015),
    'can_food': (1500, 512, .0012), 'churu': (800, 512, .001), 'ball': (1200, 512, .0012),
    'mouse_toy': (1600, 512, .001), 'wand_toy': (2000, 512, .0012), 'scratcher': (2000, 1024, .003),
    'cushion': (3000, 1024, .006), 'hideout': (4000, 1024, .008), 'litter_box': (3000, 1024, .008),
    'cat_tower_1': (6000, 2048, .004),
}
ids = ids or list(SPEC)
raw = os.path.join(out, '_raw'); os.makedirs(raw, exist_ok=True)
subprocess.run(['node', os.path.join(HERE, 'export_items.mjs'), raw, ','.join(ids)], check=True, cwd=HERE)
for i in ids:
    tris, tex, vox = SPEC[i]
    d = os.path.join(out, i)
    r = subprocess.run([sys.executable, os.path.join(HERE, 'blender_items.py'), os.path.join(raw, i + '_raw.glb'), d, i,
                        '--tris', str(tris), '--tex', str(tex), '--voxel', str(vox)], capture_output=True, text=True)
    done = [l for l in r.stdout.splitlines() if l.startswith('done') or ' raw ' in l]
    print(i, *done)
    info = json.load(open(os.path.join(raw, i + '.json')))
    info.update(game_tris=tris, texture=tex)
    json.dump(info, open(os.path.join(d, i + '.json'), 'w'), ensure_ascii=False, indent=1)
shutil.rmtree(raw)
