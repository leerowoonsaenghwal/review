# 사용자가 할 일 (출시 전)

개발은 테스트용 값과 가짜 모듈로 계속한다. 아래는 사용자의 계정이나 결정이 필요한 일이다. (무엇을 / 어디서 / 어떻게)

| # | 무엇을 | 어디서 | 어떻게 |
|---|---|---|---|
| 1 | 애플 개발자 계정 (유료 멤버십) 확인 | developer.apple.com | 팀 ID `53JSX9F7FL`가 쓰이고 있음. 연회비 갱신 상태 확인 |
| 2 | 앱 등록 (번들 ID, 앱 이름 "놀러와요 고양이섬") | App Store Connect → 나의 앱 → + | 번들 ID는 `com.rowoon.CatIsland` (unity `CatIslandSetup.cs` 에서 바꿀 수 있음), 버전 1.0.0. 연령 등급 12+ |
| 3 | 인앱 결제 상품 등록 | App Store Connect → 앱 → 인앱 구입 | 상품 ID 목록은 개발 중 `docs/RELEASE_TODO.md` 아래 '상품 ID'에 채워 둔다 |
| 4 | 광고 회사 앱 ID (AdMob 또는 AppLovin MAX) | admob.google.com | 앱 추가 → 보상형·전면 광고 단위 만들기 → ID를 알려 주면 넣는다 |
| 5 | Game Center 업적·순위표 등록 | App Store Connect → 앱 → Game Center | ID 목록은 개발 중 아래에 채워 둔다 |
| 6 | 개인정보 처리방침 게시 | 정적 웹페이지 (예: GitHub Pages, Notion 공개 페이지) | 초안은 개발 끝에 `docs/PRIVACY_POLICY.md`로 제공 |
| 7 | 실제 아이폰 테스트 | 사용자 아이폰 | **2026-10-10 개발용으로 설치됨** (lifeisgood^~^, iPhone 17 Pro, 홈 화면 '놀고섬'). 5~10분 플레이, 이상하면 알려 주기. 새 버전 다시 설치: 아이폰을 연결·잠금 해제하고 `unity/tools/install_device.sh` |
| 8 | 상표 출원 ("놀러와요 고양이섬") | 특허로 (patent.go.kr) | 상품류 9류(게임 소프트웨어), 41류(온라인 게임 제공) |
| 10 | 앱 기능 켜기 (서명에 필요) | developer.apple.com → Identifiers → 이 앱 | iCloud(키값 저장), Game Center, In-App Purchase 를 켠다. Xcode 프로젝트에는 빌드 때 자동으로 켜진다 |
| 11 | 앱 개인정보 보호 항목 입력 | App Store Connect → 앱 → 앱 개인정보 보호 | `docs/APPSTORE.md` '앱 개인정보 보호' 표대로 (광고 넣기 전: 수집 안 함) |
| 9 | 앱스토어 심사 제출 | App Store Connect | 스크린샷 `assets/store/69`·`65`, 설명·키워드·심사 메모 `docs/APPSTORE.md`, 아이콘은 빌드에 들어 있음 |

## 상품 ID (App Store Connect → 인앱 구입에 이대로 등록)
| 상품 ID | 이름 | 가격 | 종류 |
|---|---|---|---|
| `com.nolgoseom.jelly_60` | 젤리 60개 | 1,200원 | 소모품 (젤리) |
| `com.nolgoseom.jelly_330` | 젤리 330개 | 5,900원 | 소모품 (젤리) |
| `com.nolgoseom.jelly_700` | 젤리 700개 | 11,000원 | 소모품 (젤리) |
| `com.nolgoseom.jelly_1500` | 젤리 1500개 | 22,000원 | 소모품 (젤리) |
| `com.nolgoseom.jelly_4200` | 젤리 4200개 | 59,000원 | 소모품 (젤리) |
| `com.nolgoseom.season_sakura` | 벚꽃 세트 | 9,900원 | 비소모품 |
| `com.nolgoseom.season_summer` | 여름 세트 | 9,900원 | 비소모품 |
| `com.nolgoseom.season_halloween` | 할로윈 세트 | 9,900원 | 비소모품 |
| `com.nolgoseom.season_winter` | 겨울 세트 | 9,900원 | 비소모품 |
| `com.nolgoseom.star_lanterns` | 별나라 등불 꾸미기 | 3,900원 | 비소모품 |
| `com.nolgoseom.star_garden` | 별나라 구름 정원 | 5,900원 | 비소모품 |

## Game Center ID (App Store Connect → Game Center에 등록)
- 순위표: `com.nolgoseom.board.dex` (도감에 오른 품종 수, 높을수록 위)
- 업적: `com.nolgoseom.ach.first_cat`, `com.nolgoseom.ach.three_cats`, `com.nolgoseom.ach.yard`, `com.nolgoseom.ach.attend_7`, `com.nolgoseom.ach.crafter`, `com.nolgoseom.ach.photographer`, `com.nolgoseom.ach.dex_all`, `com.nolgoseom.ach.decorator_10`, `com.nolgoseom.ach.decorator_20`, `com.nolgoseom.ach.decorator_30`, `com.nolgoseom.ach.affection_2`, `com.nolgoseom.ach.affection_3`, `com.nolgoseom.ach.affection_4`, `com.nolgoseom.ach.affection_5`, `com.nolgoseom.ach.affection_6`, `com.nolgoseom.ach.affection_7`, `com.nolgoseom.ach.affection_8`, `com.nolgoseom.ach.affection_9`, `com.nolgoseom.ach.affection_10`
