"""Motion-only game file: the clips of a raw export (export_cats.py with ONLY=...) without re-finishing the body.
    python3 blender_motion.py RAW.glb OUT.fbx
Keeps the armature (same names as the finished cat, so Unity binds the clips to the game model) and the face mesh
(its blendshape curves: blink, mouth); drops the body mesh. Unity: CatArtImport puts these clips in place of the
ones in the main file with the same name (<id>__motion.fbx).
"""
import sys
import bpy
raw, out = sys.argv[1], sys.argv[2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.render.fps = 30
bpy.ops.import_scene.gltf(filepath=raw)
face = next(o for o in bpy.data.objects if o.type == 'MESH' and o.data.shape_keys)
for o in list(bpy.data.objects):
    if o.type == 'EMPTY' or (o.type == 'MESH' and o is not face): bpy.data.objects.remove(o)
face.name = 'Face'
for act in bpy.data.actions:
    clip = act.name.split('_')[0]
    act.name = ('Face_' + clip) if act.id_root == 'KEY' else clip
    print('action', act.name)
bpy.ops.export_scene.fbx(filepath=out, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
                         bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                         bake_anim_simplify_factor=0.5, path_mode='COPY', embed_textures=False, axis_forward='-Z', axis_up='Y',
                         apply_scale_options='FBX_SCALE_ALL', use_armature_deform_only=True, mesh_smooth_type='FACE')
print('done', out)
