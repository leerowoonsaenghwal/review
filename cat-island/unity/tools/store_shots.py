"""앱스토어 스크린샷 완성: Shots/store_raw/<n>_<크기>.png 위쪽에 크림색 띠 + 한 줄 문구 (docs/APPSTORE.md 6장, BRAND 색·주아 글꼴).
결과: Shots/store/<크기>/<n>.png (6.9인치 1320x2868, 6.5인치 1284x2778). 필요: pip install pillow"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent.parent
RAW, OUT = HERE / "Shots" / "store_raw", HERE / "Shots" / "store"
FONT = HERE / "Assets" / "CatIsland" / "Resources" / "Fonts" / "Jua.ttf"
CREAM, COCOA, STRAW = (0xFB, 0xF6, 0xE6), (0x6F, 0x5A, 0x40), (0xFF, 0x9A, 0xB3)
CAPTIONS = {
    "1_petting": ("살살 쓰다듬으면", "골골송"),
    "2_decorate": ("바닥도 꾸미고", "놓으면 바로 써 봐요"),
    "3_tower": ("한 층씩", "폴짝!"),
    "4_breeds": ("33가지 품종 +", "우리 집 고양이"),
    "5_guest": ("오늘의 손님,", "산책 선물"),
    "6_sunset": ("아침부터", "밤까지"),
}

for raw in sorted(RAW.glob("*_*.png")):
    name, size = raw.stem.rsplit("_", 1)
    if name not in CAPTIONS: continue
    img = Image.open(raw).convert("RGB"); W, H = img.size
    band = int(H * .2)
    out = Image.new("RGB", (W, H), CREAM)
    shot = img.crop((0, int(H * .06), W, int(H * .06) + H - band))   # (게임 화면의 위쪽 하늘 조금은 잘라 띠 자리로)
    out.paste(shot, (0, band))
    d = ImageDraw.Draw(out)
    d.rounded_rectangle((0, -60, W, band + 30), radius=60, fill=CREAM)   # (띠 아래 모서리가 둥글게)
    a, b = CAPTIONS[name]
    f = ImageFont.truetype(str(FONT), int(W * .085))
    for i, (txt, col) in enumerate(((a, COCOA), (b, (0xB8, 0x3D, 0x61)))):
        tw = d.textlength(txt, font=f)
        d.text(((W - tw) / 2, band * (.2 + .38 * i)), txt, font=f, fill=col)
    dst = OUT / size; dst.mkdir(parents=True, exist_ok=True)
    out.save(dst / f"{name}.png", optimize=True)
    print(dst / f"{name}.png")
