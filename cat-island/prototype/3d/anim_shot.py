"""Render a motion test page frame by frame into an MP4 (two panels: now | new), with labels.
  python3 anim_shot.py "<query>" out.mp4 [panel_size]
  e.g. python3 anim_shot.py "scene=jump" jump.mp4 540
The page (anim_b.html) builds the cat and its clips, then window.frame(i) draws frame i at 30 fps.
Needs the static server: python3 -m http.server 8766 (in this folder)."""
import sys, os, time, subprocess, tempfile, urllib.parse
from playwright.sync_api import sync_playwright
q, out = sys.argv[1], sys.argv[2]; N = int(sys.argv[3]) if len(sys.argv) > 3 else 540
here = os.path.dirname(os.path.abspath(__file__)); font = os.path.join(here, 'fonts/Jua.ttf')
tmp = tempfile.mkdtemp(prefix='anim_')
with sync_playwright() as p:
    b = p.chromium.launch(args=["--use-gl=swiftshader", "--enable-unsafe-swiftshader"])
    pg = b.new_page(viewport={"width": 2 * N, "height": N}); errs = []
    pg.on("pageerror", lambda e: errs.append(str(e)))
    pg.goto("http://localhost:8766/anim_b.html?size=%d&%s" % (N, urllib.parse.quote(q, safe="=&")), wait_until="commit", timeout=120000)
    t0 = time.time()
    while time.time() - t0 < 3000 and not pg.evaluate("window.__done === true") and not errs: time.sleep(1)
    if errs: print(errs[:3]); sys.exit(1)
    n = pg.evaluate("window.__frames"); print('built in', round(time.time() - t0), 's,', n, 'frames')
    cv = pg.locator("canvas")
    for i in range(n):
        pg.evaluate(f"window.frame({i})")
        cv.screenshot(path=os.path.join(tmp, f"f{i:04d}.png"))
    b.close()
label = lambda txt, x: f"drawtext=fontfile={font}:text='{txt}':x={x}:y=18:fontsize={N // 18}:fontcolor=0x5a4632:box=1:boxcolor=0xfbf6e6@0.85:boxborderw=10"
vf = ",".join([label('지금', 20), label('바뀐 움직임', N + 20)])
subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-framerate", "30", "-i", os.path.join(tmp, "f%04d.png"), "-vf", vf, "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20", out], check=True)
print(out)
