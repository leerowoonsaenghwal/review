"""Export breeds as glTF (raw, vertex-coloured, high-res) for the Blender finishing pass.
   python3 export_cats.py OUT_DIR id1,id2,...
"""
import base64, json, os, sys
from playwright.sync_api import sync_playwright
out, ids = sys.argv[1], sys.argv[2].split(',')
os.makedirs(out, exist_ok=True)
with sync_playwright() as p:
    b = p.chromium.launch(args=["--use-gl=angle", "--use-angle=swiftshader", "--enable-unsafe-swiftshader"])
    for cid in ids:
        pg = b.new_page(); errs = []
        pg.on("pageerror", lambda e: errs.append(str(e)))
        pg.goto(f"http://localhost:8766/export_cat.html?id={cid}" + (f"&only={os.environ['ONLY']}" if os.environ.get("ONLY") else ""), timeout=120000, wait_until="commit")   # (the build runs inside the page load)
        try: pg.wait_for_function("window.__done === true", timeout=21000000)   # (long-haired breeds take over 100 min)
        except Exception: print(cid, "FAILED", errs); continue
        data = base64.b64decode(pg.evaluate("window.__glb")); info = pg.evaluate("window.__info")
        open(os.path.join(out, f"{cid}_raw.glb"), "wb").write(data)
        json.dump(info, open(os.path.join(out, f"{cid}.json"), "w"), ensure_ascii=False, indent=1)
        print(cid, len(data) // 1024, "KB", info["tris"], "tris", len(info["clips"]), "clips")
        pg.close()
    b.close()
