"""Record a play2.html clip at 30 fps and save it as a GIF (and optionally MP4).
   python3 make_gif2.py out.gif "id=korean_shorthair&clip=Walk&view=34" [repeat] [--mp4] [--width 290]
"""
import os, subprocess, sys, tempfile
from playwright.sync_api import sync_playwright

out, query = sys.argv[1], sys.argv[2]
repeat = int(sys.argv[3]) if len(sys.argv) > 3 and sys.argv[3].isdigit() else 1
tmp = tempfile.mkdtemp()
with sync_playwright() as p:
    b = p.chromium.launch(args=["--use-gl=angle", "--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist"])
    pg = b.new_page(viewport={"width": 800, "height": 600})
    logs = []
    pg.on("pageerror", lambda e: logs.append(str(e)))
    pg.goto("http://localhost:8766/play2.html?" + query)
    try:
        pg.wait_for_function("window.__done === true", timeout=300000)
    except Exception:
        print("\n".join(logs)); raise
    n = pg.evaluate("window.__frames")
    k = 0
    for rep in range(repeat):
        for i in range(n):
            pg.evaluate(f"frame({i})")
            pg.locator("#cv").screenshot(path=os.path.join(tmp, f"f{k:04d}.png")); k += 1
    b.close()
src = os.path.join(tmp, "f%04d.png")
# a palette per frame: one shared palette has too few pinks for the small tongue, which then dithers into dots
subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-framerate", "30", "-i", src, "-lavfi",
                ("scale=%d:-1:flags=lanczos," % int(sys.argv[sys.argv.index("--width") + 1]) if "--width" in sys.argv else "") + "split[a][b];[a]palettegen=stats_mode=single[p];[b][p]paletteuse=new=1:dither=sierra2_4a", "-loop", "0", out], check=True)
if "--mp4" in sys.argv:
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-framerate", "30", "-i", src, "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20", out.rsplit(".", 1)[0] + ".mp4"], check=True)
print(out, k, "frames", os.path.getsize(out) // 1024, "KB")
