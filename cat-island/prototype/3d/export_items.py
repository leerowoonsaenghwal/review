"""Build every item as a game asset (docs/ART_DIRECTION.md 5장): itemkit.js parts (export_itemkit.html, one raw per
colour variant) -> blender_kit.py (closed parts, one atlas, colour / normal / AO bake, variant textures) -> assets.
   python3 export_items.py OUT_DIR [id,id,...] [--jobs 4]
Needs the static server: python3 -m http.server 8766 (in this folder).
Per item: triangle budget, texture size and bake reach (how far small baked-on detail stands off the game mesh).
The new cat towers (tower_*) are files only for now: the game keeps cat_tower_1 until the 0.4 m jump exists.
"""
import base64, json, os, subprocess, sys, time
from concurrent.futures import ThreadPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
args = sys.argv[1:]
out = os.path.abspath(args[0]); ids = args[1].split(',') if len(args) > 1 and not args[1].startswith('--') else None
JOBS = int(args[args.index('--jobs') + 1]) if '--jobs' in args else 4
SPEC = {   # id: (tris, texture, bake reach m)
    'food_bowl': (6000, 1024, .012), 'water_bowl': (4000, 1024, .004), 'milk_bowl': (4000, 1024, .004),
    'can_food': (3000, 512, .004), 'churu': (1500, 512, .003), 'ball': (3000, 512, .004),
    'mouse_toy': (3000, 512, .003), 'wand_toy': (3500, 512, .003), 'scratcher': (2500, 1024, .004),
    'cushion': (12000, 1024, .006), 'hideout': (12000, 1024, .004), 'litter_box': (8000, 1024, .012),
    'cat_tower_1': (8000, 2048, .004),
    'tower_stool': (9000, 2048, .004), 'tower_stairs': (14000, 2048, .004), 'tower_house': (18000, 2048, .004),
    'tower_tree': (14000, 2048, .004), 'tower_tall': (24000, 2048, .004),
}
ids = ids or list(SPEC)
raw = os.path.join(out, '_raw'); os.makedirs(raw, exist_ok=True)

# ---- raw parts from the browser (three.js + canvas textures), every colour variant
from playwright.sync_api import sync_playwright
infos = {}
with sync_playwright() as p:
    b = p.chromium.launch(args=['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
    pg = b.new_page(); errs = []
    pg.on('pageerror', lambda e: errs.append(str(e)))
    for i in ids:
        v, n = 0, 1
        while v < n:
            pg.goto(f'http://localhost:8766/export_itemkit.html?id={i}&variant={v}', timeout=120000); t0 = time.time()
            while time.time() - t0 < 300 and not pg.evaluate('window.__done === true') and not errs: time.sleep(.3)
            if errs or pg.evaluate('window.__err || null'): raise SystemExit(f'{i} v{v}: {errs or pg.evaluate("window.__err")}')
            info = pg.evaluate('window.__info'); n = info['variants']
            open(os.path.join(raw, f'{i}_v{v}.glb'), 'wb').write(base64.b64decode(pg.evaluate('window.__glb')))
            if v == 0: infos[i] = info
            v += 1
        print(i, 'raw', infos[i]['rawTris'], 'tris, parts', infos[i]['parts'], 'variants', n, flush=True)
    b.close()

# ---- anchors: where cats eat, lie, land (kept from items.js: the clips were fitted to them)
anchors = json.loads(subprocess.run(['node', '-e', '''
import('./items.js').then(m => { const o = {}; for (const id of process.argv[1].split(',')) if (m.ITEM_IDS.includes(id)) o[id] = m.itemField(id).anchors; console.log(JSON.stringify(o)); });
''', ','.join(ids)], cwd=HERE, capture_output=True, text=True, check=True).stdout)


def finish(i):
    tris, tex, ext = SPEC[i]
    d = os.path.join(out, i); n = infos[i]['variants']
    r = subprocess.run([sys.executable, os.path.join(HERE, 'blender_kit.py'), d, i] + [os.path.join(raw, f'{i}_v{v}.glb') for v in range(n)] +
                       ['--tris', str(tris), '--tex', str(tex), '--ext', str(ext)], capture_output=True, text=True)
    lines = [l for l in r.stdout.splitlines() if l.startswith(i + ' ') or l.startswith('done')]
    if r.returncode: lines.append('ERROR ' + r.stderr[-800:])
    info = infos[i]
    meta = {'id': i, 'ko': info['ko'], 'units': 'metres, Y up, +Z front', 'anchors': anchors.get(i) or info.get('anchors'),
            'variants': [f'{i}_color.jpg'] + [f'{i}_color_v{v}.jpg' for v in range(1, n)], 'gloss': info.get('gloss', 0),
            'parts': info['parts'], 'game_tris': tris, 'texture': tex}
    if i == 'cushion': meta['blendShapes'] = {'Press': 'the dent of a loafing cat (itemkit.js CUSHION_PRESS), weight 0..100'}
    if i == 'ball': meta['roll'] = {'pivot': [0, .056, 0], 'r': .056}
    if i.startswith('tower_'): meta['gameUse'] = 'not yet: needs the 0.4 m JumpUp and a jump down (step B)'
    json.dump(meta, open(os.path.join(d, i + '.json'), 'w'), ensure_ascii=False, indent=1)
    return i, lines


with ThreadPoolExecutor(JOBS) as ex:
    for i, lines in ex.map(finish, ids):
        print(*lines, sep='\n', flush=True)
