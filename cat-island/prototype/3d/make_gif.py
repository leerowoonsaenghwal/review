"""Export an anim.html clip as a GIF: python3 make_gif.py out.gif "id=ragdoll&clip=liedown" [frames] [fps]"""
import os, subprocess, sys, tempfile
from playwright.sync_api import sync_playwright

out, query = sys.argv[1], sys.argv[2]
n = int(sys.argv[3]) if len(sys.argv) > 3 else 48
fps = int(sys.argv[4]) if len(sys.argv) > 4 else 16
tmp = tempfile.mkdtemp()
with sync_playwright() as p:
    b = p.chromium.launch(args=["--use-gl=angle", "--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist"])
    pg = b.new_page(viewport={"width": 480, "height": 360})
    pg.goto("http://localhost:8766/anim.html?" + query)
    pg.wait_for_function("window.__done === true", timeout=90000)
    for i in range(n):
        pg.evaluate(f"frame({i / n})")
        pg.locator("#cv").screenshot(path=os.path.join(tmp, f"f{i:03d}.png"))
    b.close()
pal = os.path.join(tmp, "pal.png")
subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-framerate", str(fps), "-i", os.path.join(tmp, "f%03d.png"), "-vf", "palettegen=stats_mode=diff", pal], check=True)
subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-framerate", str(fps), "-i", os.path.join(tmp, "f%03d.png"), "-i", pal,
                "-lavfi", "paletteuse=dither=sierra2_4a", "-loop", "0", out], check=True)
print(out, os.path.getsize(out) // 1024, "KB")
