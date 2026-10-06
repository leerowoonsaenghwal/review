"""Blender finishing pass for the part-built items (itemkit.js, docs/ART_DIRECTION.md 5장): raw parts -> game asset.

    python3 blender_kit.py OUT_DIR NAME RAW_v0.glb [RAW_v1.glb ...] [--tris 8000] [--tex 1024] [--ext .004]

  1. every 'both' / 'proxy' part becomes ONE closed mesh (seams welded, degenerate faces dissolved, normals out),
     lightly decimated where it is dense; parts that would open up keep their full mesh. The parts are joined
     into one object but stay separate closed shells (shadows: ITEMS.md)
  2. one UV atlas; the painted look of all 'both' / 'detail' parts is baked onto it (colour + tangent normal:
     kibble, stitches and screws live on in the textures), then soft ambient occlusion is multiplied in
  3. colour variants: the same mesh, one more colour texture per variant (RAW_v1.glb ... -> NAME_color_v1.jpg)
  4. the cushion seat's 'Press' morph (the dent of a loafing cat) stays a shape key -> a Unity blend shape
  5. export NAME.fbx (Unity: Forward -Z, Up Y) and NAME.glb
"""
import math, os, sys
import bpy, bmesh
import numpy as np

args = sys.argv[1:]
out_dir, name = args[0], args[1]
raws = [a for a in args[2:] if a.endswith('.glb')]
opt = lambda k, d: type(d)(args[args.index(k) + 1]) if k in args else d
TRIS, TEX, EXT = opt('--tris', 8000), opt('--tex', 1024), opt('--ext', .004)
os.makedirs(out_dir, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def only(*objs, active=None):
    for o in bpy.data.objects: o.select_set(False)
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = active or (objs[0] if objs else None)


def tris(o): return sum(len(p.vertices) - 2 for p in o.data.polygons)


def open_edges(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    r = sum(1 for e in bm.edges if len(e.link_faces) != 2); bm.free(); return r


def role(o):
    for k in ('role',):
        if k in o: return o[k]
        if o.data and k in o.data: return o.data[k]
    return 'both'


def load(raw):
    """Import one raw glb: flat list of mesh objects in world space, materials turned into emission (for baking)."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=raw)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == 'MESH']
    only(*meshes); bpy.ops.object.make_single_user(object=True, obdata=True)
    for o in meshes:
        mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
    only(*meshes); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in new:
        if o.type != 'MESH': bpy.data.objects.remove(o)
    return meshes


def emissive(objs):
    """Each material: whatever feeds Base Color now feeds an Emission shader (an exact colour bake)."""
    done = set()
    for o in objs:
        for m in o.data.materials:
            if not m or m.name in done: continue
            done.add(m.name); t = m.node_tree
            bsdf = next((n for n in t.nodes if n.type == 'BSDF_PRINCIPLED'), None)
            outn = next(n for n in t.nodes if n.type == 'OUTPUT_MATERIAL')
            em = t.nodes.new('ShaderNodeEmission')
            if bsdf and bsdf.inputs['Base Color'].is_linked: t.links.new(bsdf.inputs['Base Color'].links[0].from_socket, em.inputs['Color'])
            elif bsdf: em.inputs['Color'].default_value = bsdf.inputs['Base Color'].default_value
            t.links.new(em.outputs['Emission'], outn.inputs['Surface'])


# ---------------------------------------------------------------- the game mesh from the parts
hi = load(raws[0])
lo_src = [o for o in hi if role(o) in ('both', 'proxy')]
hi = [o for o in hi if role(o) in ('both', 'detail')]
only(*lo_src); bpy.ops.object.duplicate(); lo_parts = list(bpy.context.selected_objects)
for o in lo_src:
    if role(o) == 'proxy': bpy.data.objects.remove(o)      # (proxies only receive)
hi = [o for o in hi if o.name in bpy.data.objects]
raw_tris = sum(tris(o) for o in lo_parts)
ratio = min(1, TRIS / max(1, raw_tris))
bad = 0
for o in lo_parts:
    only(o); bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=1e-5); bpy.ops.mesh.dissolve_degenerate(threshold=1e-6)
    bpy.ops.mesh.fill_holes(sides=0)                       # (surface-net solids can leave a few open edges where thin walls meet)
    bpy.ops.mesh.quads_convert_to_tris(); bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    if open_edges(o):                                      # (still not a clean shell, e.g. surface nets where thin walls meet: re-skin it
        rm = o.modifiers.new('rm', 'REMESH'); rm.mode = 'VOXEL'      #  with a voxel remesh - one closed manifold; the look is baked from the original)
        rm.voxel_size = max(o.dimensions) / 260; rm.use_smooth_shade = True
        bpy.ops.object.modifier_apply(modifier='rm')
    if ratio < .98 and tris(o) > 150 and not o.data.shape_keys:
        keep = o.data.copy(); base = max(ratio, 300 / tris(o)) if tris(o) * ratio < 300 else ratio
        for k in (1, 1.6, 2.5, 4, 6):                        # (a reduction that opens the part is undone and tried gentler)
            r = min(1, base * k)
            d = o.modifiers.new('dec', 'DECIMATE'); d.ratio = r; d.use_collapse_triangulate = True
            bpy.ops.object.modifier_apply(modifier='dec')
            if not open_edges(o) or r >= 1: break
            o.data = keep.copy()
        if open_edges(o): o.data = keep                       # (would open up: keep the full part)
    e = open_edges(o)
    if e: bad += 1; print(name, 'part not closed:', o.name, e)
only(*lo_parts, active=lo_parts[0]); bpy.ops.object.join()
low = bpy.context.active_object; low.name = name; low.data.name = name
for v in low.data.vertices:
    if v.co.z < 0: v.co.z = 0                                # (nothing under the floor)
bpy.ops.object.shade_smooth()
try: bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))   # crisp where boards meet, soft on round parts
except Exception: pass
print(name, 'parts', len(lo_parts), 'raw', raw_tris, 'game', tris(low), 'open parts', bad)

# ---------------------------------------------------------------- UVs, bake target
while low.data.uv_layers: low.data.uv_layers.remove(low.data.uv_layers[0])
low.data.uv_layers.new(name='UVMap')
only(low); bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=.003, area_weight=1.0)
# smart project flattens each island by projection: thin bands seen edge-on collapse to slivers. Keep its islands as
# seams, flatten every island properly (angle based), then pack
bpy.ops.uv.select_all(action='SELECT'); bpy.ops.uv.seams_from_islands()
bpy.ops.uv.unwrap(method='ANGLE_BASED', margin=.003)
bpy.ops.uv.pack_islands(rotate=True, margin=.003)
bpy.ops.object.mode_set(mode='OBJECT')
uvs = np.array([l.uv[:] for l in low.data.uv_layers.active.data]); print(name, 'uv span', uvs.min(0), uvs.max(0))
if os.environ.get('KIT_UV_ONLY'): sys.exit(0)
low.data.materials.clear()
mat = bpy.data.materials.new(name + '_Mat'); mat.use_nodes = True; low.data.materials.append(mat)


def target(img):
    t = mat.node_tree; n = t.nodes.new('ShaderNodeTexImage'); n.image = img
    for x in t.nodes: x.select = False
    n.select = True; t.nodes.active = n


scene.render.engine = 'CYCLES'; scene.cycles.device = 'CPU'; scene.cycles.samples = 1; scene.render.bake.margin = 6
emissive(hi)
col = bpy.data.images.new(name + '_color', TEX, TEX)
nrm = bpy.data.images.new(name + '_normal', TEX, TEX); nrm.colorspace_settings.name = 'Non-Color'
ao = bpy.data.images.new(name + '_ao', TEX // 2, TEX // 2); ao.colorspace_settings.name = 'Non-Color'
only(*hi, low, active=low)
target(col); bpy.ops.object.bake(type='EMIT', use_selected_to_active=True, cage_extrusion=EXT, max_ray_distance=EXT * 3)
target(nrm); bpy.ops.object.bake(type='NORMAL', use_selected_to_active=True, cage_extrusion=EXT, max_ray_distance=EXT * 3, normal_space='TANGENT')
for o in hi: o.hide_render = True
scene.cycles.samples = 64
only(low); target(ao); bpy.ops.object.bake(type='AO', use_selected_to_active=False)
A = np.array(ao.pixels[:], dtype=np.float32).reshape(TEX // 2, TEX // 2, 4)[:, :, 0]
A = np.repeat(np.repeat(A, 2, 0), 2, 1)


def finish(img, path):
    c = np.array(img.pixels[:], dtype=np.float32).reshape(TEX, TEX, 4)
    c[:, :, :3] *= (.62 + .38 * A)[:, :, None]                # soft contact shading (kept light: the colours stay clear)
    img.pixels[:] = c.ravel()
    img.filepath_raw = path; img.file_format = 'JPEG'; scene.render.image_settings.quality = 92; img.save()


finish(col, os.path.join(out_dir, name + '_color.jpg'))
nrm.filepath_raw = os.path.join(out_dir, name + '_normal.png'); nrm.file_format = 'PNG'; nrm.save()

# ---------------------------------------------------------------- colour variants (same mesh)
for o in hi: bpy.data.objects.remove(o)
scene.cycles.samples = 1
for k, raw in enumerate(raws[1:], 1):
    hv = [o for o in load(raw) if role(o) in ('both', 'detail')]
    for o in list(bpy.data.objects):
        if o.type == 'MESH' and o != low and o not in hv: bpy.data.objects.remove(o)
    emissive(hv)
    cv = bpy.data.images.new(f'{name}_color_v{k}', TEX, TEX)
    only(*hv, low, active=low); target(cv)
    bpy.ops.object.bake(type='EMIT', use_selected_to_active=True, cage_extrusion=EXT, max_ray_distance=EXT * 3)
    finish(cv, os.path.join(out_dir, f'{name}_color_v{k}.jpg'))
    for o in hv: bpy.data.objects.remove(o)
    print(name, 'variant', k)

# ---------------------------------------------------------------- final material, export
t = mat.node_tree
for n in list(t.nodes):
    if n.type == 'TEX_IMAGE': t.nodes.remove(n)
bsdf = t.nodes['Principled BSDF']; bsdf.inputs['Roughness'].default_value = .8
tc = t.nodes.new('ShaderNodeTexImage'); tc.image = col; t.links.new(tc.outputs['Color'], bsdf.inputs['Base Color'])
tn = t.nodes.new('ShaderNodeTexImage'); tn.image = nrm
nm = t.nodes.new('ShaderNodeNormalMap'); t.links.new(tn.outputs['Color'], nm.inputs['Color']); t.links.new(nm.outputs['Normal'], bsdf.inputs['Normal'])
only(low)
bpy.ops.export_scene.gltf(filepath=os.path.join(out_dir, name + '.glb'), export_format='GLB', export_image_format='AUTO', use_selection=True)
bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, name + '.fbx'), use_selection=True, object_types={'MESH'}, path_mode='COPY', embed_textures=True,
                         axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE')
keys = [k.name for k in low.data.shape_keys.key_blocks] if low.data.shape_keys else []
print('done', name, 'tris', tris(low), 'open parts', bad, 'shape keys', keys)
