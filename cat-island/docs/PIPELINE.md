# 작업 방식: 고양이·동작·용품·아이콘을 어떻게 만드는가

> 이 프로젝트는 일반 게임처럼 아티스트가 손으로 조각하고 애니메이터가 손으로 동작을 만드는 방식이 **아니다.** 모양과 움직임을 **수식으로 정의하고 코드로 자동 생성**하며, 3D 겉면끼리의 거리를 직접 재서 겹침·닿음을 **자동 점검**한다. 외부 서비스, 그림 생성 AI, 서버, API 키는 쓰지 않는다.
> 이 문서는 이 방식으로 이어서 작업할 사람(또는 Claude)을 위한 안내다.

## 0. 왜 이 방식인가
- 처음 조건: "3D 모델과 모션은 외부 서비스 없이 직접 만든다", 서버·API 키 없음.
- 33품종 + 사진으로 만든 고양이를 **같은 규칙**으로 만들어야 한다 → 품종은 "숫자 묶음"(귀, 얼굴, 털, 무늬…)으로만 다르다.
- 겹침 없는 출시 품질 → 사람 눈 대신 매 프레임 거리 측정으로 확인.

**일반 방식과의 차이** (나중에 사람 손을 섞을 때 참고)
| | 일반 게임 | 이 프로젝트 |
|---|---|---|
| 모양 | 원화 → 조각(ZBrush/Blender) → 면 정리·색칠 | 둥근 도형을 수식으로 이어 붙여 생성 (`catmodel.js`) |
| 품종 | 따로 만들거나 고쳐서 여러 벌 | 같은 생성기 + 품종별 숫자 (`catgen.js` BREEDS) |
| 동작 | 애니메이터가 손으로 / 모션 캡처 | 시간에 따른 관절 수식 + 자동 계산 (`catmotion.js`) |
| 검수 | 사람 눈 | 매 프레임 겉면 거리 측정 (`catcontact.js`, `qa.mjs`, `qa_items.mjs`) |

**섞어 쓰는 길**: 결과 파일(FBX/glb)은 Blender에서 그대로 열린다. 모든 고양이가 같은 크기·뼈대이므로, 기본 고양이나 핵심 동작을 사람이 Blender에서 다듬으면 33품종 모두에 쓸 수 있다. 사람 손을 섞을지는 사용자가 정한다.

## 1. 도구
| 도구 | 역할 |
|---|---|
| JavaScript + three.js 0.169 (`prototype/3d/`) | 모양 생성, 뼈대, 동작 계산, 겹침 점검. 브라우저 또는 Node |
| Blender 4.2 (`bpy`, 파이썬 모듈) | 게임용 정리: 면 줄이기, 펼치기(UV), 텍스처 굽기, FBX/glb 내보내기 |
| Playwright + Chromium | 사람 없이 페이지를 열어 고양이를 만들고 파일을 받거나 그림을 찍는다 |
| Python 3.11 | 순서대로 돌리는 스크립트 |

## 2. 고양이 모델 (`catmodel.js`, `sdfmesh.js`, `catgen.js`)
1. **거리 함수로 조각**: 몸통·머리·다리·꼬리를 타원·둥근 원뿔 등(`sdEllipsoid`, `sdRoundCone`)으로 정의하고 부드럽게 합친다(`smin`). 이음새 없는 한 덩어리.
2. **겉면 뽑기**: `surfaceNets`로 삼각형 그물을 뽑고 `relax`로 고른다 (약 10만 삼각형).
3. **뼈대와 스키닝**: 등뼈·목·머리·다리·꼬리·귀·혀(5개) 뼈(`BONE_PARENTS`). 겉면의 점마다 따라갈 뼈와 무게.
4. **얼굴 메시**: 눈·코·입·혀·수염·발바닥 젤리는 따로 만들어 붙인다. 표정 변형 2개(깜빡임, 입 벌림).
5. **털 무늬**: `catgen.js`의 `makeCoat`가 무늬(고등어, 클래식, 포인트, 삼색…)를 수식으로 칠한다.
6. **품종 = 숫자 묶음**: `catgen.js`의 `BREEDS[i].shape / coat`.
7. **같은 크기·같은 뼈대 (중요)**: `frameShape()`가 모든 고양이를 같은 크기·몸길이·목길이·다리길이로 맞춘다. 먼치킨만 짧은 다리(legLen 0.5). 몸 두께(bodyBulk, legBulk)는 ±10%, 배는 표준, 가슴은 +10% 이내. 품종 특징은 얼굴·귀·눈·털·꼬리·무늬로 낸다. 이 원칙을 깨는 변경은 사용자에게 먼저 묻는다.
8. **사진 → 고양이** (`photo2cat.js`): 사진에서 털 색·무늬를 읽고(`analyzeCoat`) 가까운 품종을 고른다(`suggestBreeds`). 사진은 기기 밖으로 나가지 않는다.

## 3. 동작 (`catmotion.js`)
- **자세 = 숫자 묶음** `P` (예: `FLx/FLy/FLz` 앞발 목표, `hdPitch` 고개, `tailWave`…). `applyPose`가 뼈에 적용.
- **동작 = 시간 → 자세 함수**. `makeClips(rig)`가 21개 동작을 만든다: Idle, Walk, Trot, Gallop, JumpUp, SitDown, Sit, StandUp, LieDown, Loaf, Sleep, FallAsleep, GroomFace, ScratchEar, NibbleClaws, Stretch, Drink, LickLips, Flop, FlopIdle, PawBat.
- **발 위치 자동 계산(역운동학)**: 발 목표를 주면 다리 관절 각도를 계산. 다리 각도로 직접 줄 수도 있다(`<다리>fk`).
- **닿게 하기** `touch` / `touchBest`: 혀↔앞발, 앞발↔얼굴처럼 닿아야 할 때, 실제 스킨 메시의 거리를 재며 관절 값을 최적화(감쇠 최소제곱). `guard`로 다른 부위가 파고들지 않게 막는다.
- **겹침 밀어내기** `settle`: 다리가 몸·머리에 박히면 밀어내고, 몸이 바닥 밑이면 올린다. `withSettle`이 동작 전체에 적용(매 프레임 검사 후 필요한 프레임만 추가 보정).
- **마지막 보정** (`add()` 안): `floorFix`(바닥·캣타워 판 위로), `tongueFix`(그루밍 혀), `drinkFix`(마시는 혀가 그릇 표면에 닿게).
- **용품 맞춤**: 마시기(`Drink`)는 그릇 표면 높이 `BOWL_SURF`, 점프(`JumpUp`)는 캣타워 판 높이 `DECK_STEP`, 앞발 장난(`PawBat`)은 쥐돌이 윗면 `TOY_TOP`에 맞춰 계산한다. 쥐돌이 위치는 `clip.toy`.
- **점검 후 빼기 (verify-or-drop)**: 그루밍 동작과 `CHECKED_CLIPS`(Gallop→Trot, PawBat→Idle)는 만든 직후 30fps로 검사해 기준을 넘으면 그 품종에서 뺀다. 이유는 `clips.json`의 `skippedClips`. 앞발 장난은 실패하면 턱 들기(`chinLift`)로 한 번 더 시도한다.
- **굽기** `bakeClip`: 30fps 뼈 움직임으로 저장.

## 4. 겹침 점검 기준 (`catcontact.js`, `qa.mjs`, `qa_items.mjs`)
- `catcontact.js`: 스킨 메시를 CPU로 계산하고, 격자로 가까운 삼각형을 찾아 부호 있는 거리를 잰다(안쪽 판정은 부위 상자 + 광선 교차로 확인).
- 기준: 다리·발이 몸·머리에 **4 mm** 넘게 박히면 실패, 바닥 아래 4 mm 실패, 닿아야 할 때 **3 mm** 이내, 7 mm 넘게 눌리면 실패. 긴 털·곱슬 털은 털 속 겹침을 경고로만.
- `qa_items.mjs`: 그릇 마시기, 캣타워 점프, 방석, 숨숨집, 쥐돌이 치기 장면을 용품의 정확한 거리 함수(`itemField`)로 검사. 용품 안으로 3 mm 넘게 들어가면 실패(방석 15 mm, 숨숨집·쥐돌이 12 mm까지 허용).

## 5. 게임 파일로 내보내기
1. `export_cats.py OUT/raw <품종>`: Playwright가 `export_cat.html`을 열어 고양이 생성 → 동작 계산 → 원본 glb + 정보(json).
2. `blender_finish.py`: 몸 14,000·얼굴 약 10,500 삼각형으로 줄이고, 펼치고, 털 텍스처(2048)와 요철(1024)을 구워 FBX(Unity 축)·glb로 내보낸 뒤 다시 불러와 확인.
3. 결과: `assets/cats/<품종>.glb / .fbx / .clips.json` (`clips.json`에 동작 목록, 반복 여부, 빠진 동작, 용품 위치).
- 한 품종 30분~4시간(긴 털이 느림). 여러 품종은 코어 수만큼 동시에.

## 6. 용품 (`items.js`, `export_items.mjs`, `export_items.py`, `blender_items.py`)
1. 고양이와 같은 거리 함수 방식: 도형을 더하고(U), 부드럽게 더하고(SU), 깎는다(CUT). 사료 알갱이 같은 반복 요소도 코드로.
2. 크기는 고양이 비율에 맞춘다(`scale`). 고양이가 쓰는 높이는 상수로 고정: `BOWL_SURF`, `TOY_TOP`, `DECK_STEP`.
3. Blender: 다시 감싸기(voxel remesh) → 면 줄이기 → 구멍 메우기 → 펼치기 → 색·요철·그늘 굽기 → FBX/glb.
4. 내보낸 뒤 **열린 모서리 0, 겹친 모서리 0** (닫힌 메시) 확인 → 그림자가 새지 않는다.
5. 용품 모양을 바꾸면 `qa_items.mjs`로 고양이와 다시 점검한다. (예: 캣타워 테두리를 깎는 둥근 처리 때문에 판이 2.5 mm 높아져 팔꿈치가 묻혔던 일)

## 7. 앱 아이콘·로고 그림
- 게임의 실제 고양이 glb를 게임과 같은 빛으로 렌더링: `icon_render.html` + `icon_shot.py`.
  ```bash
  cd cat-island/prototype/3d && python3 -m http.server 8766 &
  python3 icon_shot.py korean_shorthair "clip=Sit&t=2&shot=island&yaw=0.35&zoom=1.3" icon.png 1024
  # 얼굴: shot=face&ty=0.12&dist=1.5&up=0.08  /  배경 투명: bg=none  /  섬 없이: noisland=1
  ```
- 등록용 규격과 완성 파일: `assets/app_icon/README.md` (1024 PNG 투명 없음, iOS 18 어두운·색조 변형, 구글 512).

## 8. 이어서 작업할 때의 규칙
- 동작을 고치면 **반드시** `makeClips` 결과의 `skippedClips`와 `qa_items.mjs`로 확인한 뒤 커밋한다.
- 수정은 모든 품종에 영향을 준다. 대표 품종(코리안 숏헤어, 페르시안, 메인쿤, 오리엔탈, 스핑크스, 스코티시 폴드, 브리티시, 먼치킨)으로 먼저 확인한다.
- 이미 만든 33종 파일과 코드가 어긋나지 않게, 동작 코드를 바꾸면 영향을 받는 품종은 다시 만든다.
- 한 번 확인한 방법(검사 스크립트 등)은 `prototype/3d/`에 남기고 이 문서에 적는다.
