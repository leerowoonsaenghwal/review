# Cat Island (가칭)

동물의 숲 그래픽 스타일의 **방치형 고양이 키우기** 게임 (iPhone, Unity).
핵심 기능은 **사진으로 나만의 고양이 만들기**이고, 무지개다리를 건넌 고양이를 위한 **별나라** 추억 공간이 있다.

- `docs/CAT_CATALOG.md`: 품종과 털 무늬 조사, 3D 고양이 파라미터 설계, 사진 → 고양이 변환 계획
- `prototype/3d/`: three.js로 만든 3D 외형 프로토타입
  - 파라미터 방식 고양이 생성기
  - 척추 2마디 + 다리 IK 리그 (실제 고양이 관절 방향)
  - 품종 33종과 털 무늬 12종 카탈로그
  - 마을 그래픽 시안
  - 사진 → 고양이 변환 (`photo.html`, `photo2cat.js`)
  - 품종별 전신 점검 (`fullbody.html`)
  - 기본 동작 세트: 서기·걷기·앉기·식빵·그루밍·잠자기·기지개 (`motions.html`, `anim.html`, GIF는 `make_gif.py`)

![로꼬 사진 → 3D](docs/images/rocco_photo_to_3d.png)

![마을 시안](docs/images/ac3d.png)
