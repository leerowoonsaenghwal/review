"""Render a brand picture page (brand_symbol.html ...) to a PNG, supersampled 2x.
  python3 brand_shot.py <page.html> "<query>" out.png [w h]
  e.g. python3 brand_shot.py brand_symbol.html "view=icon" icon.png 1024 1024
Needs the static server: python3 -m http.server 8766 (in this folder). See docs/BRAND.md."""
import sys, time, json, urllib.parse
from PIL import Image
from playwright.sync_api import sync_playwright
page, q, out = sys.argv[1], sys.argv[2], sys.argv[3]; w, h = (int(sys.argv[4]), int(sys.argv[5])) if len(sys.argv) > 5 else (1024, 1024)
W, H = w * 2, h * 2
with sync_playwright() as p:
    b = p.chromium.launch(args=["--use-gl=swiftshader", "--enable-unsafe-swiftshader"])
    pg = b.new_page(viewport={"width": W, "height": H}); errs = []
    pg.on("pageerror", lambda e: errs.append(str(e)))
    pg.goto("http://localhost:8766/%s?size=%d&W=%d&H=%d&%s" % (page, W, W, H, urllib.parse.quote(q, safe="=&")), wait_until="commit", timeout=120000)
    t0 = time.time()
    while time.time() - t0 < 1500 and not pg.evaluate("window.__done === true") and not errs: time.sleep(.5)
    print(json.dumps(pg.evaluate("window.__info || null")), errs[:3])
    pg.locator("canvas").screenshot(path=out, omit_background=True); b.close()
Image.open(out).resize((w, h), Image.LANCZOS).save(out)
