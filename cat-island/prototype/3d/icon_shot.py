"""Render an app icon / logo picture of a finished cat with the game's light.
  python3 icon_shot.py <breed> "<query>" out.png [size]
  e.g. python3 icon_shot.py korean_shorthair "clip=Sit&t=2&shot=island&yaw=0.35&zoom=1.3" icon.png 1024
Needs the static server: python3 -m http.server 8766 (in this folder). See docs/PIPELINE.md."""
import sys, time, json, shutil, os
from playwright.sync_api import sync_playwright
breed, q, out = sys.argv[1], sys.argv[2], sys.argv[3]; n = int(sys.argv[4]) if len(sys.argv) > 4 else 1024
here = os.path.dirname(os.path.abspath(__file__)); os.makedirs(os.path.join(here, 'out/icon'), exist_ok=True)
shutil.copy(os.path.join(here, '../../assets/cats/%s.glb' % breed), os.path.join(here, 'out/icon/%s.glb' % breed))
with sync_playwright() as p:
    b = p.chromium.launch(args=["--use-gl=swiftshader", "--enable-unsafe-swiftshader"])
    pg = b.new_page(viewport={"width": n, "height": n}); errs = []
    pg.on("pageerror", lambda e: errs.append(str(e)))
    pg.goto("http://localhost:8766/icon_render.html?size=%d&glb=out/icon/%s.glb&%s" % (n, breed, q))
    t0 = time.time()
    while time.time() - t0 < 300 and not pg.evaluate("window.__done === true") and not errs: time.sleep(.5)
    print(json.dumps(pg.evaluate("window.__info || null")), errs[:3])
    pg.locator("canvas").screenshot(path=out, omit_background=True); b.close()
