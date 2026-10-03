"""Blender finishing pass for items: raw dense vertex-coloured mesh -> game asset.

    python3 blender_items.py RAW.glb OUT_DIR NAME [--tris 3000] [--tex 1024] [--voxel 0.002]

  1. voxel remesh: one clean closed manifold surface (no edges shared by 3+ faces where thin parts touch, so
     shadow volumes / shadow maps and backface culling behave)
  2. decimate to a mobile triangle budget, smooth shading
  3. UV unwrap; bake the colours from the dense raw (selected-to-active) and a tangent-space normal map that
     keeps the small detail (kibble, sisal rope, cardboard flutes) on the light mesh
  4. bake ambient occlusion on the game mesh and multiply it into the colour (soft contact shading inside the
     bowl, under the decks, in the igloo: reads well even with the cheapest mobile shader)
  5. export NAME.fbx (Unity: Forward -Z, Up Y) and NAME.glb
"""
import math, os, sys
import bpy

args = sys.argv[1:]
raw, out_dir, name = args[0], args[1], args[2]
opt = lambda k, d: type(d)(args[args.index(k) + 1]) if k in args else d
TRIS, TEX, VOX = opt('--tris', 3000), opt('--tex', 1024), opt('--voxel', .002)
os.makedirs(out_dir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=raw)
scene = bpy.context.scene
hi = next(o for o in bpy.data.objects if o.type == 'MESH')


def only(*objs, active=None):
    for o in bpy.data.objects: o.select_set(False)
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = active or objs[0]


def tris(o): return sum(len(p.vertices) - 2 for p in o.data.polygons)


# ---------------------------------------------------------------- remesh + decimate
only(hi); bpy.ops.object.duplicate(); low = bpy.context.active_object; low.name = name
rm = low.modifiers.new('remesh', 'REMESH'); rm.mode = 'VOXEL'; rm.voxel_size = VOX; rm.use_smooth_shade = True
bpy.ops.object.modifier_apply(modifier='remesh')
n0 = tris(low)
dec = low.modifiers.new('decimate', 'DECIMATE'); dec.ratio = min(1, TRIS / max(1, n0)); dec.use_collapse_triangulate = True
bpy.ops.object.modifier_apply(modifier='decimate')
# weld and fill any hole the collapse left (a closed surface is what shadows need)
only(low); bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=1e-5); bpy.ops.mesh.dissolve_degenerate(threshold=1e-5)   # (zero-area faces: the exporter drops them, leaving holes)
bpy.ops.mesh.fill_holes(sides=0); bpy.ops.mesh.quads_convert_to_tris()
bpy.ops.mesh.normals_make_consistent(inside=False)
bpy.ops.object.mode_set(mode='OBJECT')
# nothing under the floor (the voxel grid can round a flat bottom a hair down; glTF Y up is Blender Z up)
for v in low.data.vertices:
    if v.co.z < 0: v.co.z = 0
bpy.ops.object.shade_smooth()
import bmesh
def holes(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    r = sum(1 for e in bm.edges if len(e.link_faces) != 2); bm.free(); return r
print(name, 'open/non-manifold edges after cleanup:', holes(low))
print(name, 'raw', tris(hi), 'remeshed', n0, 'game', tris(low))

# ---------------------------------------------------------------- UVs
only(low); bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=.004, area_weight=1.0)
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------------------------------------------------------- bake colour, normal, AO
scene.render.engine = 'CYCLES'; scene.cycles.device = 'CPU'; scene.cycles.samples = 4
scene.render.bake.margin = 6
src = bpy.data.materials.new('bake_src'); src.use_nodes = True
nt = src.node_tree; nt.nodes.clear()
ca = nt.nodes.new('ShaderNodeVertexColor'); ca.layer_name = hi.data.color_attributes[0].name
em = nt.nodes.new('ShaderNodeEmission'); out = nt.nodes.new('ShaderNodeOutputMaterial')
nt.links.new(ca.outputs['Color'], em.inputs['Color']); nt.links.new(em.outputs['Emission'], out.inputs['Surface'])
hi.data.materials.clear(); hi.data.materials.append(src)
mat = bpy.data.materials.new(name + '_Mat'); mat.use_nodes = True
low.data.materials.clear(); low.data.materials.append(mat)
col = bpy.data.images.new(name + '_color', TEX, TEX)
nrm = bpy.data.images.new(name + '_normal', TEX, TEX); nrm.colorspace_settings.name = 'Non-Color'
ao = bpy.data.images.new(name + '_ao', TEX // 2, TEX // 2); ao.colorspace_settings.name = 'Non-Color'


def target(img):
    t = mat.node_tree; n = t.nodes.new('ShaderNodeTexImage'); n.image = img
    for x in t.nodes: x.select = False
    n.select = True; t.nodes.active = n


ext = max(VOX * 3, .008)                 # (the decimated surface can sit several mm off the dense one over bumpy kibble)
only(hi, low, active=low)
target(col); bpy.ops.object.bake(type='EMIT', use_selected_to_active=True, cage_extrusion=ext, max_ray_distance=ext * 3)
target(nrm); bpy.ops.object.bake(type='NORMAL', use_selected_to_active=True, cage_extrusion=ext, max_ray_distance=ext * 3, normal_space='TANGENT')
hi.hide_render = True
scene.cycles.samples = 64
only(low); target(ao); bpy.ops.object.bake(type='AO', use_selected_to_active=False)

# multiply AO into the colour (kept soft: 0.55 .. 1)
import numpy as np
c = np.array(col.pixels[:], dtype=np.float32).reshape(TEX, TEX, 4)
a = np.array(ao.pixels[:], dtype=np.float32).reshape(TEX // 2, TEX // 2, 4)[:, :, 0]
a = np.repeat(np.repeat(a, 2, 0), 2, 1)
c[:, :, :3] *= (.55 + .45 * a)[:, :, None]
col.pixels[:] = c.ravel()

# ---------------------------------------------------------------- final material, export
t = mat.node_tree
for n in list(t.nodes):
    if n.type == 'TEX_IMAGE': t.nodes.remove(n)
bsdf = t.nodes['Principled BSDF']; bsdf.inputs['Roughness'].default_value = .8
tc = t.nodes.new('ShaderNodeTexImage'); tc.image = col; t.links.new(tc.outputs['Color'], bsdf.inputs['Base Color'])
tn = t.nodes.new('ShaderNodeTexImage'); tn.image = nrm
nm = t.nodes.new('ShaderNodeNormalMap'); t.links.new(tn.outputs['Color'], nm.inputs['Color']); t.links.new(nm.outputs['Normal'], bsdf.inputs['Normal'])
bpy.data.objects.remove(hi)
col.filepath_raw = os.path.join(out_dir, col.name + '.jpg'); col.file_format = 'JPEG'; scene.render.image_settings.quality = 92; col.save()
nrm.filepath_raw = os.path.join(out_dir, nrm.name + '.png'); nrm.file_format = 'PNG'; nrm.save()
bpy.ops.export_scene.gltf(filepath=os.path.join(out_dir, name + '.glb'), export_format='GLB', export_image_format='AUTO')
bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, name + '.fbx'), object_types={'MESH'}, path_mode='COPY', embed_textures=True,
                         axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE')
print('done', name, 'tris', tris(low))
