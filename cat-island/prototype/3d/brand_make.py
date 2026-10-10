"""놀고섬 브랜드 파일 만들기 (2026-10-10 새 그림체): 게임과 같은 3D 심볼 + 주아 글꼴 워드마크.

    1) Unity 에서 심볼 찍기 (게임과 같은 고양이·재질·빛, 배경 투명):
       Unity -batchmode -projectPath unity -runTests -testPlatform PlayMode -testFilter CatIsland.Tests.BrandArt
       → unity/Shots/brand/symbol_raw.png (2048)
    2) python3 brand_make.py   → assets/app_icon/*, assets/brand/*

앱 아이콘 규격: assets/app_icon/README.md. 색·글꼴: docs/BRAND.md.
"""
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
RAW = os.path.join(ROOT, 'unity', 'Shots', 'brand', 'symbol_raw.png')
ICON = os.path.join(ROOT, 'assets', 'app_icon')
BRAND = os.path.join(ROOT, 'assets', 'brand')
FONT = os.path.join(HERE, 'fonts', 'Jua.ttf')

CREAM, COCOA, GRASS, FOREST, STRAWBERRY, SKY, SEA = '#FBF6E6', '#6F5A40', '#8FCA5E', '#3F7A2E', '#FF7F9F', '#BFE6F6', '#86CFE6'


def hexrgb(h, a=255):
    h = h.lstrip('#'); return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (a,)


def trim(im, pad=0):
    b = im.getbbox(); im = im.crop(b)
    if pad:
        out = Image.new('RGBA', (im.width + 2 * pad, im.height + 2 * pad), (0, 0, 0, 0)); out.alpha_composite(im, (pad, pad)); im = out
    return im


def fit(im, w, h):
    s = min(w / im.width, h / im.height); return im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))), Image.LANCZOS)


def sky(size, top='#9FD9F2', bottom='#DFF4FB'):
    """하늘 배경: 위는 맑은 하늘색, 아래로 갈수록 밝게 + 몽실한 구름 둘."""
    w, h = size; t, b = hexrgb(top), hexrgb(bottom)
    im = Image.new('RGBA', size)
    px = im.load()
    for y in range(h):
        u = y / (h - 1); c = tuple(round(t[i] + (b[i] - t[i]) * u) for i in range(4))
        for x in range(w): px[x, y] = c
    a = Image.new('L', size, 0); d = ImageDraw.Draw(a)   # (구름: 흰색 + 모양만 알파로 - 흐림에 검은 테두리가 섞이지 않게)
    for cx, cy, s in ((.18, .17, 1.0), (.84, .26, .8)):
        for dx, dy, r in ((-.07, .01, .055), (0, -.015, .075), (.075, .005, .06), (0, .03, .06)):
            x, y, rr = (cx + dx * s) * w, (cy + dy * s) * h, r * s * w
            d.ellipse([x - rr, y - rr * .8, x + rr, y + rr * .8], fill=235)
    clouds = Image.new('RGBA', size, (255, 255, 255, 255)); clouds.putalpha(a.filter(ImageFilter.GaussianBlur(w * .002)))
    im.alpha_composite(clouds)
    return im


def soft_shadow(sym, offset, blur, alpha=70):
    a = sym.split()[3].point(lambda v: alpha if v > 8 else 0)
    sh = Image.new('RGBA', sym.size, hexrgb(COCOA, 0)); sh.putalpha(a)
    sh = sh.filter(ImageFilter.GaussianBlur(blur))
    out = Image.new('RGBA', (sym.width + offset[0] + blur * 4, sym.height + offset[1] + blur * 4), (0, 0, 0, 0))
    out.alpha_composite(sh, (offset[0] + blur * 2, offset[1] + blur * 2)); out.alpha_composite(sym, (blur * 2, blur * 2))
    return out


def rounded_mask(n, r):
    S = 4; m = Image.new('L', (n * S, n * S), 0); ImageDraw.Draw(m).rounded_rectangle([0, 0, n * S - 1, n * S - 1], radius=r * S, fill=255)
    return m.resize((n, n), Image.LANCZOS)


# ------------------------------------------------------------------ 워드마크 (주아, 게임 버튼과 같은 크림 + 코코아 테두리)
def text_layer(text, size, fill, stroke, stroke_w, shadow=True):
    f = ImageFont.truetype(FONT, size)
    l, t, r, b = f.getbbox(text, stroke_width=stroke_w)
    w, h = r - l + stroke_w * 2 + 40, b - t + stroke_w * 2 + 40
    im = Image.new('RGBA', (w, h), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    o = (20 - l + stroke_w, 20 - t + stroke_w)
    if shadow:   # (아래로 떨어진 코코아 그림자: 말랑한 입체감)
        sh = Image.new('RGBA', (w, h), (0, 0, 0, 0)); ImageDraw.Draw(sh).text((o[0], o[1] + size * .06), text, font=f, fill=hexrgb(COCOA, 120), stroke_width=stroke_w, stroke_fill=hexrgb(COCOA, 120))
        im.alpha_composite(sh.filter(ImageFilter.GaussianBlur(size * .015)))
    d.text(o, text, font=f, fill=hexrgb(fill), stroke_width=stroke_w, stroke_fill=hexrgb(stroke))
    # (윗면 하이라이트: 글자 위쪽 절반을 아주 살짝 밝게)
    hl = Image.new('RGBA', (w, h), (0, 0, 0, 0)); ImageDraw.Draw(hl).text((o[0], o[1] - size * .025), text, font=f, fill=(255, 255, 255, 70))
    mask = Image.new('L', (w, h), 0); ImageDraw.Draw(mask).text(o, text, font=f, fill=255)
    hl.putalpha(Image.composite(hl.split()[3], Image.new('L', (w, h), 0), mask))
    im.alpha_composite(hl)
    return trim(im)


def badge(text, size):
    """'놀러와요': 딸기우유 둥근 배지에 크림 글자 (게임의 분홍 버튼)."""
    t = text_layer(text, size, CREAM, COCOA, max(2, size // 14), shadow=False)
    pw, ph = t.width + size * .9, t.height + size * .5
    im = Image.new('RGBA', (int(pw) + 20, int(ph) + 30), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    lw = max(3, size // 12)
    d.rounded_rectangle([10, 18, 10 + pw, 18 + ph], radius=ph / 2, fill=hexrgb(COCOA, 110))           # (그림자)
    d.rounded_rectangle([10, 10, 10 + pw, 10 + ph], radius=ph / 2, fill=hexrgb(STRAWBERRY), outline=hexrgb(COCOA), width=lw)
    im.alpha_composite(t, (int(10 + (pw - t.width) / 2), int(10 + (ph - t.height) / 2)))
    return trim(im)


def ears(w, color=CREAM):
    """'섬' 위에 앉는 작은 고양이 귀 둘 (통통한 둥근 삼각형, 분홍 속귀)."""
    S = 4; W = w * S; h = int(W * .42)
    im = Image.new('RGBA', (W, h + 8 * S), (0, 0, 0, 0)); d = ImageDraw.Draw(im); lw = max(4, W // 18)
    for x0 in (0, W * .58):
        pts = [(x0, h), (x0 + W * .08, h * .3), (x0 + W * .21, 2 * S), (x0 + W * .34, h * .3), (x0 + W * .42, h)]
        d.polygon(pts, fill=hexrgb(color)); d.line(pts, fill=hexrgb(COCOA), width=lw, joint='curve')
        d.polygon([(x0 + W * .11, h - lw), (x0 + W * .21, h * .3), (x0 + W * .31, h - lw)], fill=hexrgb('#FFB6C8'))
    return im.resize((w, im.height // S), Image.LANCZOS)


def wordmark(scale=1.0):
    big = text_layer('고양이섬', int(300 * scale), CREAM, COCOA, int(16 * scale))
    small = badge('놀러와요', int(110 * scale))
    e = ears(int(big.width * .17))
    W = max(big.width, small.width) + int(40 * scale); H = small.height + big.height + e.height
    im = Image.new('RGBA', (W, H + 20), (0, 0, 0, 0))
    im.alpha_composite(small, (int(20 * scale), 0))
    by = small.height - int(14 * scale); bx = (W - big.width) // 2
    # (귀는 마지막 글자 '섬' 위 가운데에, 글자 뒤로 살짝 묻히게)
    im.alpha_composite(e, (bx + int(big.width * .875) - e.width // 2, by + int(big.height * .06) - int(e.height * .5)))
    im.alpha_composite(big, (bx, by))
    return trim(im, 8)


def main():
    os.makedirs(ICON, exist_ok=True); os.makedirs(BRAND, exist_ok=True)
    raw = Image.open(RAW).convert('RGBA')
    sym = trim(raw)
    sym_pad = trim(raw, 24)
    sym_pad.save(os.path.join(BRAND, 'symbol.png'))
    for name, col in (('symbol_mono_cocoa.png', COCOA), ('symbol_mono_cream.png', CREAM)):
        a = sym_pad.split()[3]; m = Image.new('RGBA', sym_pad.size, hexrgb(col)); m.putalpha(a); m.save(os.path.join(BRAND, name))

    # 앱 아이콘 (1024, 투명 없음): 하늘 + 심볼 (모서리가 둥글게 잘려도 귀·얼굴이 안쪽)
    N = 1024
    icon = sky((N, N))
    s = fit(sym, N * .84, N * .74); s = soft_shadow(s, (0, 14), 10, 60)   # (홈 화면에서 둥글게 잘려도 섬 가장자리가 남게)
    icon.alpha_composite(s, ((N - s.width) // 2, int(N * .56 - s.height / 2)))
    icon.convert('RGB').save(os.path.join(ICON, 'AppIcon-1024.png'))
    icon.convert('RGB').resize((512, 512), Image.LANCZOS).save(os.path.join(ICON, 'PlayStore-512.png'))
    # 어두운 모드: 배경 투명 (시스템이 어둡게 깐다)
    dark = Image.new('RGBA', (N, N), (0, 0, 0, 0)); s2 = fit(sym, N * .84, N * .76); dark.alpha_composite(s2, ((N - s2.width) // 2, int(N * .54 - s2.height / 2)))
    dark.save(os.path.join(ICON, 'AppIcon-1024-dark.png'))
    # 색조 모드: 흑백, 검은 바탕
    g = s2.convert('LA'); lum = g.split()[0].point(lambda v: min(255, int(v * 1.15)))
    tint = Image.new('RGBA', (N, N), (0, 0, 0, 255)); gl = Image.merge('RGBA', (lum, lum, lum, s2.split()[3])); tint.alpha_composite(gl, ((N - s2.width) // 2, int(N * .54 - s2.height / 2)))
    tint.convert('L').convert('RGB').save(os.path.join(ICON, 'AppIcon-1024-tinted.png'))
    # 켜는 화면 로고: 둥근 아이콘 512 (투명 바탕)
    L = icon.resize((512, 512), Image.LANCZOS); L.putalpha(rounded_mask(512, int(512 * .225))); L.save(os.path.join(ICON, 'LaunchLogo.png'))
    # 홈 화면 모습 미리보기: 밝은·어두운·색조, 큰 것·작은 것
    prev = Image.new('RGBA', (1500, 560), hexrgb('#E9E4D8'))
    dprev = Image.new('RGBA', (500, 560), hexrgb('#1C1C1E')); prev.alpha_composite(dprev, (500, 0)); prev.alpha_composite(dprev, (1000, 0))
    for i, (img, bgc) in enumerate(((icon, None), (dark, '#2C2C2E'), (tint, None))):
        for j, n in enumerate((300, 120)):
            t = img.resize((n, n), Image.LANCZOS).convert('RGBA')
            if bgc: base = Image.new('RGBA', (n, n), hexrgb(bgc)); base.alpha_composite(t); t = base
            t.putalpha(rounded_mask(n, int(n * .225)))
            prev.alpha_composite(t, (i * 500 + (40 if j == 0 else 360), 120 if j == 0 else 210))
    prev.convert('RGB').save(os.path.join(ICON, 'preview.png'))

    # 워드마크·조합
    wm = wordmark(); wm.save(os.path.join(BRAND, 'wordmark.png'))
    short = text_layer('놀고섬', 300, CREAM, COCOA, 16); short = trim(short, 8); short.save(os.path.join(BRAND, 'wordmark_short.png'))
    sv = fit(sym, wm.width * .8, 10 ** 6)
    v = Image.new('RGBA', (max(sv.width, wm.width), sv.height + wm.height - 30), (0, 0, 0, 0)); v.alpha_composite(sv, ((v.width - sv.width) // 2, 0)); v.alpha_composite(wm, ((v.width - wm.width) // 2, sv.height - 30))
    trim(v, 10).save(os.path.join(BRAND, 'lockup_vertical.png'))
    sh = fit(sym, 10 ** 6, wm.height * 1.15)
    h = Image.new('RGBA', (sh.width + wm.width + 40, max(sh.height, wm.height)), (0, 0, 0, 0)); h.alpha_composite(sh, (0, (h.height - sh.height) // 2)); h.alpha_composite(wm, (sh.width + 40, (h.height - wm.height) // 2))
    trim(h, 10).save(os.path.join(BRAND, 'lockup_horizontal.png'))
    # 첫 화면 예시: 게임 화면 위에 세로 조합
    shot = os.path.join(ROOT, 'unity', 'Shots', 'store_raw', '2_decorate_69.png')
    if os.path.exists(shot):
        bg = Image.open(shot).convert('RGBA').resize((1320 // 2, 2868 // 2))
        veil = Image.new('RGBA', bg.size, hexrgb(CREAM, 70)); bg.alpha_composite(veil)
        lk = fit(Image.open(os.path.join(BRAND, 'lockup_vertical.png')), bg.width * .82, bg.height * .45)
        bg.alpha_composite(lk, ((bg.width - lk.width) // 2, int(bg.height * .1)))
        bg.convert('RGB').save(os.path.join(BRAND, 'mock_title_screen.png'))
    print('ok', sym.size, wm.size)


if __name__ == '__main__':
    main()
