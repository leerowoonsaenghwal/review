# Unity 프로젝트: 파이프라인 고양이·용품을 게임에 넣는 방법

`cat-island/unity/` 는 Unity 6.3 LTS(6000.3.25f1) + URP 프로젝트다. 기획서 4부의 1단계 "손맛 시제품"(고양이 1마리, 쓰다듬기, 방석, 밥)에서 시작했고, 그림은 파이프라인(`docs/PIPELINE.md`)이 만든 고양이·용품을 그대로 쓴다.

## 1. 원본은 하나: `cat-island/assets/`

| 할 일 | 명령 |
|---|---|
| 넣을 품종·용품 정하기 | `unity/tools/art_manifest.json` (지금: 코리안 숏헤어, 사료 그릇, 방석, 캣타워 1단) |
| Unity로 가져오기 | `python3 unity/tools/sync_art.py` |

- 고양이 `.fbx`·`.clips.json`, 용품 `.fbx`·색·노멀·`.json` 을 `unity/Assets/CatIsland/Art/` 로 복사한다.
- **FBX에는 얼굴 색 아틀라스(눈·코·입 색)가 들어 있지 않아** glb에서 꺼낸다 (`<품종>_face.png`).
- 이 사본(바이너리)은 git에 올리지 않는다. Unity 가져오기 설정(`.meta`)만 올려 GUID가 바뀌지 않게 한다.
- 파이프라인으로 고양이를 다시 만들면 `sync_art.py` 를 다시 돌리면 된다.

## 2. 가져오기 자동화 (`Editor/CatArtImport.cs`, 메뉴 CatIsland/Import Art)

`CatIslandSetup.Run` 과 모든 빌드가 먼저 부른다.

| 단계 | 내용 |
|---|---|
| 리그 | Generic, 움직임 기준 뼈 `Cat_<품종>/Root`. 루트 이동은 모두 뽑아내 동작은 제자리에서 재생하고, 이동은 게임 코드가 한다 |
| 동작 | 이름에서 `Cat_<품종>|` 접두어를 떼고, `clips.json` 의 `loop` 대로 반복 표시. `skippedClips` 동작은 Animator에 넣지 않는다 |
| 재질 | 내장 텍스처를 꺼내 `CatIsland/SoftLit`(무광, 부드러운 명암) 재질로 바꿔 끼운다. 털 = 코트 텍스처 + 노멀맵, 눈 = 작은 하이라이트, 눈 반짝임 = 빛과 무관하게 밝게 |
| Animator | `Locomotion` 블렌드 (Idle 0 / Walk 0.40 / Trot 1.06 / Gallop 2.27 m/s, 클립의 실제 속도라 발이 미끄러지지 않음) + 나머지 동작마다 상태 하나 |
| 기록 (`Resources/Art/Cats/<품종>_info.json`) | JumpUp 루트 곡선(앞으로 간 거리·높이, 30fps), `Drink.drink.bowl`, `itemSpots.cushion`, 발라당 자세에서 배가 향하는 방향(클립을 재생해 잼) |
| 프리팹 | `Resources/Art/Cats/<품종>.prefab`, `Resources/Art/Items/<용품>.prefab` |

## 3. 게임 코드에서 쓰는 법

- `CatRig`: 자세(서기·앉기·식빵·잠·발라당)를 전환 동작으로 잇는다 (SitDown, StandUp, LieDown, FallAsleep, Flop). 동작 위에 깜빡임(Blink), 기분 좋게 감은 눈, 입 벌림(MouthOpen), 고개 돌리기를 더한다. 쓰다듬기 판정 영역(머리 구, 몸 캡슐)은 스킨 메시 정점에서 자동으로 맞춘다.
- `CatBrain`: 용품과 맞추는 자리는 모두 파이프라인 값을 따른다.
  - 밥: 그릇 중심이 고양이 기준 `Drink.drink.bowl` 에 오게 서서 `Drink` 를 재생한다. 혀끝이 사료 표면(3 cm)에 닿는다.
  - 방석: 방석 중심이 몸통 가운데 아래(`itemSpots.cushion`)에 오게 하고, 고양이를 방석 `anchors.top` 만큼 올린다 → `LieDown` → `Loaf` → `FallAsleep` → `Sleep`.
  - 캣타워: 판 중심에서 점프 거리만큼 앞에서 출발해 JumpUp 루트 곡선을 그대로 따라 판 높이(`decks[0].y`)에 착지한다. 내려올 때는 같은 곡선의 포물선 부분만 쓰고 기준 높이를 거꾸로 바꾼다.
  - 발라당: 구르는 동안 배가 카메라를 보도록 몸을 튼다 (배 방향은 가져올 때 잰 값).

## 4. 점검과 빌드

```bash
cd cat-island/unity
tools/run_tests.sh            # EditMode 17 + PlayMode 13 (+ 스크린샷 16장 → Shots/)
tools/build_ios.sh sim        # 시뮬레이터 빌드 → 설치 → 실행
tools/build_ios.sh device     # 실기기용 Xcode 프로젝트 → Builds/iOS-Device (서명은 Xcode에서)
```

PlayMode 점검 중 용품 맞춤:
- 마실 때 그릇 위치가 `clips.json` 값과 3 cm 이내
- 잘 때 방석 위치 5 cm 이내, 높이 1 cm 이내
- 캣타워 판 위 1 cm 이내 착지, 내려와서 바닥 높이 0

## 5. 알아 둘 것

- Unity 6의 iOS **시뮬레이터 런타임은 개발판만 있다** (`debug=True` 정상). 시뮬레이터 Metal은 MSAA 렌더 패스를 지원하지 않아 시뮬레이터에서만 끈다.
- 첫 Xcode 빌드는 IL2CPP C++ 컴파일 때문에 몇 분 걸린다 (멈춘 것이 아님).
- 캣타워 카펫 텍스처는 원래 아주 연한 하늘색이라 밝은 햇빛에서 거의 흰색으로 보인다. 색 보정(톤 매핑)을 넣을지는 미정.
- 캣타워에서 **내려오는 동작은 점프해 오르기 클립을 재사용**한 것이라, `qa_items.mjs` 의 겹침 점검을 받지 않았다. 전용 동작(JumpDown)을 파이프라인에 추가하는 것이 좋다.
