"""Write assets/cats/<id>.clips.json from an export_cats.py info file (OUT/<id>.json).

    python3 cat_clips_json.py OUT/<id>.json ASSETS_DIR

Adds the fixed facts of the game asset (blend shapes, tongue bones, units, triangle counts after
blender_finish.py) to the clip list, skipped clips and item spots the export measured.
"""
import json, os, sys

src, out_dir = sys.argv[1], sys.argv[2]
info = json.load(open(src))
doc = {
    "id": info["id"], "ko": info["ko"], "bones": info["bones"],
    "clips": info["clips"], "groomRoutine": info["groomRoutine"], "skippedClips": info["skippedClips"],
    "itemSpots": info["itemSpots"],
    "sculpt_tris": info.get("tris"),
    "game_tris": {"Body": 14000, "Face": "~10500 (eyes, nose, mouth, tongue, whiskers, toe beans)"},
    "units": "metres, +Z forward, Y up (glTF); clips sampled at 30 fps",
    "blendshapes": ["Blink", "MouthOpen"],
    "tongue_bones": ["Tongue1", "Tongue2", "Tongue3", "Tongue4", "Tongue5"],
}
dst = os.path.join(out_dir, f"{info['id']}.clips.json")
json.dump(doc, open(dst, "w"), ensure_ascii=False, indent=1)
print(dst, len(doc["clips"]), "clips,", len(doc["skippedClips"]), "skipped")
