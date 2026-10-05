# 터미널에 붙여 넣을 작업 지시문: 동물의 숲 방향 적용

아래 상자 안의 글을 그대로 터미널의 Claude Code에 붙여 넣는다.

```
cat-island 프로젝트에서 확정된 새 디자인 방향(동물의 숲 방향)을 게임 전체에 적용해 줘.

먼저 읽을 것:
- cat-island/CLAUDE.md (작업 규칙: 한국어로 보고, 출시 품질, 생김새·기획이 바뀌는 결정은 먼저 묻기)
- cat-island/docs/ART_DIRECTION.md (이번 작업의 기준. 시안 그림: docs/images/style_sample_ac.png)
- cat-island/docs/PIPELINE.md (고양이·동작·용품을 코드로 만들고 자동 점검하는 방법)
- cat-island/docs/UNITY.md, cat-island/docs/BRAND.md (UI 색·글꼴·말투)

할 일 (ART_DIRECTION.md 9장 순서대로):
1. prototype/3d/catmodel.js: buildCatModel의 style 'ac'를 기본으로 바꾸고, 이전 모습은 style 'classic'으로 남겨 줘. photo2cat.js로 만드는 사진 고양이도 같은 모습이 되게.
2. 대표 8품종(코리안 숏헤어, 페르시안, 메인쿤, 오리엔탈, 스핑크스, 스코티시 폴드, 브리티시, 먼치킨)으로 makeClips의 skippedClips, qa.mjs, qa_items.mjs를 확인해 줘. 다리가 굵어지고 머리가 커져서 그루밍·귀 긁기·마시기·앞발 장난·점프 착지가 달라질 수 있어. 깨진 동작을 고치고, 빠지는 동작이 이전보다 늘지 않게 해 줘.
3. blender_finish.py: 털 노멀맵 세기를 절반 이하로. 가능하면 털 무늬를 텍스처 칸마다 무늬 수식으로 직접 계산해서 줄무늬 가장자리를 깔끔하게.
4. 33품종 전부 다시 만들어 assets/cats/를 바꾸고 unity/tools/sync_art.py를 돌려 줘. 여러 품종은 코어 수만큼 동시에.
5. 용품: 색을 맑고 진하게, 나뭇결·천 무늬 같은 질감을 그려 넣어 줘 (items.js, blender_items.py). 크기와 고양이가 쓰는 높이(BOWL_SURF, TOY_TOP, DECK_STEP)는 바꾸지 마. 다시 내보낸 뒤 qa_items.mjs.
6. Unity: ART_DIRECTION.md 2장의 빛·톤 매핑(Neutral)·안개, 4장의 풀밭 텍스처, 계단식 언덕(절벽), 뭉툭한 나무, 꽃, 바다와 해변, 멀어질수록 땅이 둥글게 휘는 효과(모든 세상 재질에 같은 휨)를 넣어 줘. 기존 테스트(tools/run_tests.sh)가 통과해야 해.
7. 그림 확인: 각 품종을 시안 장면(prototype/3d/style_compare.html side=ac)에서 찍어 한 장에 모아 보여 줘. 검은 고양이처럼 수염 선이 안 보이는 경우는 밝은 회색으로. Unity 화면 스크린샷도.

지킬 것:
- 동물의 숲의 캐릭터·나뭇잎 마크·휴대폰 화면·글꼴·음악·특정 가구 모양은 흉내 내지 마 (ART_DIRECTION.md 8장).
- 모든 고양이는 같은 크기·같은 뼈대(먼치킨만 짧은 다리) 원칙 유지.
- 단계마다 무엇을 했는지 / 어디까지 됐는지 / 다음에 무엇을 하는지 한국어로 짧게 알려 주고, 단계가 끝날 때마다 커밋해 줘.
- 시안과 다르게 바꿔야 할 것 같으면(예: 다리를 더 굵게) 먼저 나에게 물어봐.
```
