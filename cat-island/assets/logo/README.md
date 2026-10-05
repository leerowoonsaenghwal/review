# 로고 (게임 제목 글자)

게임과 같은 3D 그림체로 만든 제목 글자다. 크림색 둥근 글자 + 나무색 테두리 + 작은 새싹(섬의 야자수를 줄인 것).
글자 모양에서 가장자리까지의 거리를 재서 둥글게 부풀린 3D 면을 만들고, 게임과 같은 빛으로 찍었다 (`prototype/3d/wordmark.html`).

| 파일 | 형식 | 쓰는 곳 |
|---|---|---|
| `title_logo.png` | PNG, 배경 투명 | **게임 첫 화면 제목.** 고양이가 글자판 뒤에서 얼굴을 내민다 |
| `wordmark.png` | PNG, 배경 투명 | 글자만. 앱스토어 홍보 그림, 로딩 화면, 작은 자리 |
| `lockup_horizontal.png` | PNG, 배경 투명 | 앱 아이콘 + 글자 가로 배치. 홍보·소개 자료 |
| `preview_title.png` | — | 제목을 하늘색 배경에 놓았을 때의 모습 (확인용) |

**지킨 기준**
- 배경 투명, 바닥 그림자 없음 (어떤 배경 위에 놓아도 그림자가 떠 보이지 않게). 글자와 고양이 사이의 그림자는 있다.
- 글꼴: 주아체 (Jua, SIL Open Font License 1.1 — 상업용 게임에 써도 되고, 글꼴 파일을 팔지만 않으면 된다). 허가 문서: `prototype/3d/fonts/OFL.txt`.
- 고양이는 게임의 실제 코리안 숏헤어 3D 파일.

**다시 만들기**
```bash
cd cat-island/prototype/3d && python3 -m http.server 8766 &
python3 wordmark_shot.py "bg=none" wordmark.png 2400 840
cp ../../assets/cats/korean_shorthair.glb out/icon/ks.glb
python3 wordmark_shot.py "bg=none&cat=out/icon/ks.glb&catx=-.3&cats=3.1&catz=-.65&catyaw=-.2" title.png 2400 1800
```
(글자 바꾸기 `text=`, 테두리 색 `rimc=`/`rimc2=`, 글자 색 `fc=`/`fc2=`, 기울기 `tilt=`, 새싹 없이 `leaf=0`. 투명 영역은 잘라서 쓴다.)
