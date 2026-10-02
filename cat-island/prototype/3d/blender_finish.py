"""Blender finishing pass: raw high-res cat (vertex colours) -> game asset.

    python3 blender_finish.py RAW.glb OUT_DIR NAME [--tris 14000] [--tex 2048]

  1. Body: decimate to a mobile-friendly triangle count (skin weights are kept)
  2. UV unwrap (smart project)
  3. bake the coat colours (high -> low, selected-to-active) and a tangent-space normal map that keeps the
     sculpted detail (toe beans, muzzle edge, fur clumps) on the lighter mesh
  4. Face: keeps the generator's colour atlas (hand-laid UVs; an automatic unwrap shreds the whiskers)
  5. export NAME.fbx (Unity: Forward -Z, Up Y, all actions as takes) and NAME.glb (glTFast)
Runs headless with the `bpy` module (pip install bpy).
"""
import math, os, sys
import bpy, bmesh

args = sys.argv[1:]
raw, out_dir, name = args[0], args[1], args[2]
opt = lambda k, d: type(d)(args[args.index(k) + 1]) if k in args else d
TRIS, TEX = opt('--tris', 14000), opt('--tex', 2048)
os.makedirs(out_dir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.render.fps = 30          # the clips are authored at 30 fps; Blender's default 24 would resample them
bpy.ops.import_scene.gltf(filepath=raw)
scene = bpy.context.scene
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
body = bpy.data.objects['Body']
# the face mesh is the one with blendshapes (the importer can leave an empty 'Face' node next to it)
face = next(o for o in bpy.data.objects if o.type == 'MESH' and o.data.shape_keys)
for o in list(bpy.data.objects):
    if o.type == 'EMPTY' or (o.type == 'MESH' and o not in (body, face)): bpy.data.objects.remove(o)
face.name = 'Face'
arm.data.pose_position = 'REST'
for a in bpy.data.actions: print('action', a.name)


def only(*objs, active=None):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = active or objs[0]


def unwrap(o, margin=.004):
    only(o)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=margin, area_weight=1.0)
    bpy.ops.object.mode_set(mode='OBJECT')


def emission_from_colors(o):
    """material that emits the mesh's vertex colours (bake source)"""
    m = bpy.data.materials.new('bake_src'); m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    ca = nt.nodes.new('ShaderNodeVertexColor'); ca.layer_name = o.data.color_attributes[0].name
    em = nt.nodes.new('ShaderNodeEmission'); out = nt.nodes.new('ShaderNodeOutputMaterial')
    nt.links.new(ca.outputs['Color'], em.inputs['Color']); nt.links.new(em.outputs['Emission'], out.inputs['Surface'])
    return m


def image_target(mat, img):
    nt = mat.node_tree
    n = nt.nodes.new('ShaderNodeTexImage'); n.image = img
    for x in nt.nodes: x.select = False
    n.select = True; nt.nodes.active = n
    return n


scene.render.engine = 'CYCLES'; scene.cycles.device = 'CPU'; scene.cycles.samples = 4
scene.render.bake.margin = 6

# ---------------------------------------------------------------- body: decimate, unwrap, bake
only(body); bpy.ops.object.duplicate(); low = bpy.context.active_object; low.name = 'Body_LOD0'
ntri = sum(len(p.vertices) - 2 for p in low.data.polygons)
dec = low.modifiers.new('decimate', 'DECIMATE'); dec.ratio = min(1, TRIS / ntri); dec.use_collapse_triangulate = True
only(low); bpy.ops.object.modifier_move_to_index(modifier='decimate', index=0); bpy.ops.object.modifier_apply(modifier='decimate')
print('body tris', ntri, '->', sum(len(p.vertices) - 2 for p in low.data.polygons))
unwrap(low)
coat = bpy.data.images.new(f'{name}_coat', TEX, TEX); nrm = bpy.data.images.new(f'{name}_normal', TEX // 2, TEX // 2, float_buffer=False)
nrm.colorspace_settings.name = 'Non-Color'
src = emission_from_colors(body)
body.data.materials.clear(); body.data.materials.append(src)
lowmat = bpy.data.materials.new(f'{name}_Coat'); lowmat.use_nodes = True
low.data.materials.clear(); low.data.materials.append(lowmat)
only(body, low, active=low)
image_target(lowmat, coat)
bpy.ops.object.bake(type='EMIT', use_selected_to_active=True, cage_extrusion=.006, max_ray_distance=.02)
image_target(lowmat, nrm)
bpy.ops.object.bake(type='NORMAL', use_selected_to_active=True, cage_extrusion=.006, max_ray_distance=.02, normal_space='TANGENT')
# final coat material: colour + normal map
nt = lowmat.node_tree
for n in list(nt.nodes):
    if n.type == 'TEX_IMAGE': nt.nodes.remove(n)
bsdf = nt.nodes['Principled BSDF']; bsdf.inputs['Roughness'].default_value = .85
tc = nt.nodes.new('ShaderNodeTexImage'); tc.image = coat; nt.links.new(tc.outputs['Color'], bsdf.inputs['Base Color'])
tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = nrm
nm = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(tn.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], bsdf.inputs['Normal'])
bpy.data.objects.remove(body); low.name = 'Body'
# colour as JPEG (small), normal map as PNG (JPEG blocks would show up as bumps)
coat.filepath_raw = os.path.join(out_dir, coat.name + '.jpg'); coat.file_format = 'JPEG'; scene.render.image_settings.quality = 92; coat.save()
nrm.filepath_raw = os.path.join(out_dir, nrm.name + '.png'); nrm.file_format = 'PNG'; nrm.save()

# ---------------------------------------------------------------- face: keeps its hand-laid colour atlas (UVs come from the generator)
for slot in face.material_slots:
    m = slot.material
    if m and 'Highlight' in m.name:            # unlit white catch-lights: emissive so they stay white in any shader
        m.use_nodes = True
        bsdf = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if bsdf:
            bsdf.inputs['Base Color'].default_value = (1, 1, 1, 1)
            bsdf.inputs['Emission Color'].default_value = (1, 1, 1, 1); bsdf.inputs['Emission Strength'].default_value = 1.0

# ---------------------------------------------------------------- export
arm.data.pose_position = 'POSE'
bpy.ops.export_scene.gltf(filepath=os.path.join(out_dir, name + '.glb'), export_format='GLB', export_animations=True,
                          export_animation_mode='ACTIONS', export_morph=True, export_morph_animation=True, export_skins=True,
                          export_image_format='AUTO', export_force_sampling=True)
# FBX takes are named after Blender actions: tidy them to the clip names (body) and Face_<clip> (blendshapes)
for act in bpy.data.actions:
    clip = act.name.split('_')[0]
    act.name = ('Face_' + clip) if act.id_root == 'KEY' else clip
bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, name + '.fbx'), object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
                         bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                         bake_anim_simplify_factor=0.5, path_mode='COPY', embed_textures=True, axis_forward='-Z', axis_up='Y',
                         apply_scale_options='FBX_SCALE_ALL', use_armature_deform_only=True, mesh_smooth_type='FACE')
print('done', name, 'body tris', sum(len(p.vertices) - 2 for p in bpy.data.objects['Body'].data.polygons))
