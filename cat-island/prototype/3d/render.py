import sys
from playwright.sync_api import sync_playwright
with sync_playwright() as p:
    b=p.chromium.launch(args=["--use-gl=angle","--use-angle=swiftshader","--enable-unsafe-swiftshader","--ignore-gpu-blocklist"])
    pg=b.new_page(viewport={"width":1356,"height":700}, device_scale_factor=1)
    logs=[]; pg.on("console", lambda m: logs.append(m.text)); pg.on("pageerror", lambda e: logs.append("ERR "+str(e)))
    pg.goto("http://localhost:8765/"+sys.argv[2])
    try: pg.wait_for_function("window.__done === true", timeout=90000)
    except Exception as e: print("TIMEOUT", e)
    pg.wait_for_timeout(500)
    pg.screenshot(path=sys.argv[1], full_page=True); b.close()
    print("\n".join(logs[-10:]))
