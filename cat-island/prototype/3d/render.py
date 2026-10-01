import sys
from playwright.sync_api import sync_playwright
with sync_playwright() as p:
    b=p.chromium.launch(args=["--use-gl=angle","--use-angle=swiftshader","--enable-unsafe-swiftshader","--ignore-gpu-blocklist"])
    w=int(sys.argv[3]) if len(sys.argv)>3 else 1720
    pg=b.new_page(viewport={"width":w,"height":400}, device_scale_factor=1)
    logs=[]; pg.on("console", lambda m: logs.append(m.text)); pg.on("pageerror", lambda e: logs.append("ERR "+str(e)))
    pg.goto("http://localhost:8766/"+sys.argv[2])
    try: pg.wait_for_function("window.__done === true", timeout=90000)
    except Exception as e: print("TIMEOUT", e)
    pg.wait_for_timeout(500)
    pg.screenshot(path=sys.argv[1], full_page=True); b.close()
    print("\n".join(logs[-10:]))
