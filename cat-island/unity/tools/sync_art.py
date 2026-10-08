#!/usr/bin/env python3
"""cat-island/assets/ 의 고양이·용품 파일을 Unity 프로젝트(Assets/CatIsland/Art)로 가져온다.

원본은 assets/ 하나뿐이다. 여기 사본은 git에 올리지 않고(.gitignore), Unity 설정(.meta)만 올린다.
파이프라인(export_cats.py, export_items.py)으로 고양이·용품을 다시 만든 뒤 이 스크립트를 돌리면 된다.
"""
import json
import shutil
import struct
import sys
from pathlib import Path

UNITY = Path(__file__).resolve().parent.parent
ASSETS = UNITY.parent / "assets"
ART = UNITY / "Assets" / "CatIsland" / "Art"


def copy(src: Path, dst: Path) -> bool:
    if not src.exists():
        print(f"  없음: {src}")
        return False
    dst.parent.mkdir(parents=True, exist_ok=True)
    if dst.exists() and dst.stat().st_size == src.stat().st_size and dst.stat().st_mtime >= src.stat().st_mtime:
        return True
    shutil.copy2(src, dst)
    print(f"  복사: {src.relative_to(ASSETS)}")
    return True


def extract_face_atlas(glb: Path, dst: Path) -> bool:
    """FBX에는 얼굴 색 아틀라스(눈·코·입 색)가 들어 있지 않아 glb에서 꺼낸다 (FaceGloss/Lid 재질이 쓰는 그림)."""
    if not glb.exists():
        print(f"  없음: {glb}")
        return False
    data = glb.read_bytes()
    jlen = struct.unpack("<I", data[12:16])[0]
    gltf = json.loads(data[20:20 + jlen])
    bin_start = 20 + jlen + 8
    mat = next((m for m in gltf["materials"] if m["name"] == "FaceGloss"), None)
    if mat is None:
        print(f"  FaceGloss 재질 없음: {glb.name}")
        return False
    img = gltf["images"][gltf["textures"][mat["pbrMetallicRoughness"]["baseColorTexture"]["index"]]["source"]]
    bv = gltf["bufferViews"][img["bufferView"]]
    png = data[bin_start + bv.get("byteOffset", 0): bin_start + bv.get("byteOffset", 0) + bv["byteLength"]]
    if dst.exists() and dst.read_bytes() == png:
        return True
    dst.parent.mkdir(parents=True, exist_ok=True)
    dst.write_bytes(png)
    print(f"  얼굴 아틀라스: {dst.name}")
    return True


def main() -> int:
    manifest = json.loads((UNITY / "tools" / "art_manifest.json").read_text())
    ok = True
    for cat in manifest["cats"]:
        for ext in (".fbx", ".clips.json"):
            ok &= copy(ASSETS / "cats" / f"{cat}{ext}", ART / "Cats" / f"{cat}{ext}")
        ok &= extract_face_atlas(ASSETS / "cats" / f"{cat}.glb", ART / "Cats" / f"{cat}_face.png")
        # 사진 고양이 털 색 (CatCoat): 털 마스크·색 칸, 같은 몸에 입히는 다른 털 (korean_shorthair__tuxedo 등). 없으면 건너뜀 (예전 에셋)
        res_cats = UNITY / "Assets" / "CatIsland" / "Resources" / "Art" / "Cats"
        for f in sorted((ASSETS / "cats").glob(f"{cat}_coatmask.png")) + sorted((ASSETS / "cats").glob(f"{cat}__*")) + sorted((ASSETS / "cats").glob(f"{cat}.coat.json")):
            copy(f, res_cats / f.name.replace(".coat.json", "_slots.json"))
    for item in manifest["items"]:
        for name in (f"{item}.fbx", f"{item}_color.jpg", f"{item}_normal.png", f"{item}.json"):
            ok &= copy(ASSETS / "items" / item / name, ART / "Items" / item / name)
        for var in sorted((ASSETS / "items" / item).glob(f"{item}_color_v*.jpg")):   # 색 바꾸기 (같은 메시, 색 텍스처만 다름)
            ok &= copy(var, ART / "Items" / item / var.name)
    # 소리: assets/sounds/<묶음>/*.wav → Resources/Sounds/<묶음>/ (게임이 이름 앞부분으로 고른다: meow_, chirp_, nip_, purr_)
    snd_dst = UNITY / "Assets" / "CatIsland" / "Resources" / "Sounds"
    for wav in sorted(list((ASSETS / "sounds").glob("*/*.wav")) + list((ASSETS / "sounds").glob("*/*.ogg"))):   # (배경음악은 ogg: music_render.py)
        ok &= copy(wav, snd_dst / wav.parent.name / wav.name)
    print("완료" if ok else "일부 파일이 없습니다")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
