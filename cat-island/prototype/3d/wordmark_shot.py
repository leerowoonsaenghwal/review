"""Render the 3D title lettering (wordmark.html) to a PNG (transparent unless bg= is given).
  python3 wordmark_shot.py "<query>" out.png [w h]
  e.g. python3 wordmark_shot.py "text=고양이 섬&tilt=.42" title.png 2048 720
Needs the static server: python3 -m http.server 8766 (in this folder). See docs/PIPELINE.md."""
import sys, time, json, urllib.parse
from PIL import Image
from playwright.sync_api import sync_playwright
q, out = sys.argv[1], sys.argv[2]; w, h = (int(sys.argv[3]), int(sys.argv[4])) if len(sys.argv) > 4 else (2048, 720)
W, Hh = w * 2, h * 2   # rendered at twice the size, then shrunk: smooth edges
with sync_playwright() as p:
    b = p.chromium.launch(args=["--use-gl=swiftshader", "--enable-unsafe-swiftshader"])
    pg = b.new_page(viewport={"width": W, "height": Hh}); errs = []
    pg.on("pageerror", lambda e: errs.append(str(e)))
    pg.goto("http://localhost:8766/wordmark.html?w=%d&h=%d&%s" % (W, Hh, urllib.parse.quote(q, safe="=&")))
    t0 = time.time()
    while time.time() - t0 < 300 and not pg.evaluate("window.__done === true") and not errs: time.sleep(.5)
    print(json.dumps(pg.evaluate("window.__info || null")), errs[:3])
    pg.locator("canvas").screenshot(path=out, omit_background=True); b.close()
Image.open(out).resize((w, h), Image.LANCZOS).save(out)
