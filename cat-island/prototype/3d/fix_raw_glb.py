"""Remove broken empty face parts from raw cat glbs (export_cats.py output) before blender_finish.py.
   python3 fix_raw_glb.py RAW.glb [...]
A face material group with no triangles (a breed without eyelids: 'Lid') was written by the exporter as a primitive
with no index list, i.e. every face vertex taken in order as triangles - a sheet of junk triangles across the face
that Blender then imports (and welds into the whiskers). Such primitives are dropped; the rest is unchanged.
catmodel.js no longer makes empty groups, so new exports do not need this.
"""
import json, struct, sys

def fix(path):
    data = open(path, 'rb').read()
    jl = struct.unpack('<I', data[12:16])[0]
    j = json.loads(data[20:20 + jl]); rest = data[20 + jl:]
    dropped = 0
    for m in j['meshes']:
        if len(m['primitives']) > 1:
            keep = [p for p in m['primitives'] if 'indices' in p]
            dropped += len(m['primitives']) - len(keep); m['primitives'] = keep
    if not dropped: return 0
    js = json.dumps(j, separators=(',', ':')).encode()
    js += b' ' * ((4 - len(js) % 4) % 4)
    out = data[:12] + struct.pack('<I', len(js)) + b'JSON' + js + rest
    out = out[:8] + struct.pack('<I', len(out)) + out[12:]
    open(path, 'wb').write(out)
    return dropped

for p in sys.argv[1:]:
    print(p.split('/')[-1], 'dropped', fix(p))
