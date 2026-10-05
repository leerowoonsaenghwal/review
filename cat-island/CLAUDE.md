# 놀러와요 고양이섬 (놀고섬) — Claude 작업 규칙

이 폴더는 아이폰용 고양이 키우기 게임 **놀러와요 고양이섬**(줄임말 놀고섬, 폴더 이름은 예전 이름 그대로 cat-island)이다. 여기서 일하는 Claude는 아래 규칙을 항상 지킨다.

## 꼭 지킬 것
- **모든 대답과 보고는 한국어로.** 영어 용어는 되도록 쉬운 우리말로 풀어 쓴다. (코드, 명령, 파일 이름은 그대로)
- **진행 상황을 자주, 짧게 알린다.** 지금 무엇을 하는지 / 어디까지 됐는지 / 다음에 무엇을 하는지.
- **출시 품질, 오류 없음.** 고양이와 용품이 겹치거나 깨져 보이면 안 되고, 그림자는 자연스러워야 한다. 자동 점검(`qa.mjs`, `qa_items.mjs`)을 통과한 결과만 넣는다.
- **서버 없음, API 키 없음.** 3D 모델과 동작은 외부 서비스 없이 직접 만든다.
- **게임의 생김새나 기획이 바뀌는 결정은 먼저 묻는다.**
- 저장소의 다른 부분(리뷰 답글 서비스)은 건드리지 않는다.
- 작업 결과는 자주 커밋한다. 커밋 메시지는 무엇을 왜 바꿨는지.

## 먼저 읽을 문서
| 문서 | 내용 |
|---|---|
| `docs/GAME_PLAN_FULL.md` | **게임 기획서 전체** (게임 기획 + 수익 + 디자인 스타일 + 만드는 조건). 모든 작업의 기준 |
| `docs/PIPELINE.md` | **작업 방식**: 고양이·동작·용품·아이콘을 코드로 생성하고 자동 점검하는 방법. 개발 전에 반드시 읽는다 |
| `docs/PROJECT_BRIEF.md` | 지금까지 만든 것, 결정 기록, 지금 상태와 다음 할 일 |
| `docs/GAME_MODEL.md` | 고양이 모델·뼈대·동작 21종, Unity에서 쓰는 법 |
| `docs/ITEMS.md` | 용품 13종, 고양이와 함께 놓는 법, 그림자 설정 |
| `docs/ENGINE.md` | 엔진: Unity 6 + URP |
| `docs/ART_DIRECTION.md` | **디자인 방향 (확정): 동물의 숲 방향.** 고양이·땅·나무·바다·용품·빛의 기준. `ART_STYLE.md`보다 우선 |
| `docs/BRAND.md` | **브랜드 정의서**: 이름 표기, 로고, 색, 글꼴, 말투. 화면·문구·홍보물을 만들 때 기준 |
| `docs/UNITY.md` | Unity 프로젝트(`unity/`): 고양이·용품 가져오기, 동작 연결, 점검·빌드 명령 |

## 핵심 원칙 (기획서 요약)
- 동물의 숲 같은 그림체(맑고 선명한 색, 인형 같은 고양이, 그린 듯한 질감, 둥근 세상: `docs/ART_DIRECTION.md`), 방치형. 벌주지 않는다 / 고양이가 주인공 / 매일 조금씩 다르다.
- **모든 고양이는 같은 크기·같은 뼈대** (먼치킨만 짧은 다리). 품종 특징은 얼굴·귀·눈·털·꼬리·무늬와 몸 두께(±10%)로. 코드: `prototype/3d/catmodel.js`의 `frameShape`.
- 결제 상품은 젤리 묶음, 계절 세트, 별나라 꾸미기뿐. **광고 제거·월간 패스는 팔지 않는다.** 보상형 광고 중심, 배너 없음, 별나라에 광고 없음.

## 작업 방식 요약 (자세히: `docs/PIPELINE.md`)
- 손으로 조각·애니메이션하지 않는다. 모양과 동작을 **수식으로 정의해 코드로 생성**하고, 3D 겉면끼리 거리를 재서 **겹침·닿음을 자동 점검**한다.
- 품종 = 숫자 묶음 (`catgen.js` BREEDS). 모든 고양이는 `frameShape`로 같은 크기·뼈대.
- 동작을 고치면 대표 품종으로 `makeClips`의 `skippedClips`와 `qa_items.mjs`를 확인한 뒤 커밋한다.

## 폴더
- `prototype/3d/`: 제작 도구 (three.js). 고양이 모델(`catmodel.js`), 동작(`catmotion.js`), 접촉 계산(`catcontact.js`), 용품(`items.js`), 사진→고양이(`photo2cat.js`), 점검(`qa.mjs`, `qa_items.mjs`), 내보내기(`export_cats.py`, `blender_finish.py`, `export_items.py`)
- `assets/cats/`: 품종별 게임 파일 (`.glb`, `.fbx`, `.clips.json`)
- `assets/items/`: 용품 게임 파일
- `unity/`: Unity 6.3 LTS 게임 프로젝트. `tools/sync_art.py`로 `assets/`를 가져오고, `tools/run_tests.sh`, `tools/build_ios.sh`로 점검·빌드
- `docs/`: 기획·설명 문서

## 자주 쓰는 명령
```bash
cd cat-island/prototype/3d
npm install                                   # three 0.169
python3 -m http.server 8766 &                 # 시안 페이지·내보내기에 필요
node qa.mjs <품종> [동작,...] [--fps 30]      # 동작 겹침 점검
node qa_items.mjs <품종> [--only 장면]        # 용품과 함께 점검
python3 export_cats.py OUT/raw <품종>          # 고양이 1종 (30분~4시간)
python3 blender_finish.py OUT/raw/<품종>_raw.glb OUT/final/<품종> <품종>
python3 export_items.py OUT_DIR [이름,...]     # 용품
python3 icon_shot.py <품종> "clip=Sit&t=2&shot=island&yaw=0.35&zoom=1.3" out.png 1024   # 앱 아이콘 그림
```
필요한 것: Node 20 이상, Python 3.11, `pip install bpy==4.2.0 playwright==1.49.1`, `python3 -m playwright install chromium`.
고양이 1종을 만드는 데 시간이 오래 걸리므로, 여러 종은 컴퓨터 코어 수만큼 동시에 돌린다.
