# 인스타그램 분석 리포트 지침서 (Directive Layer)

본 문서는 인스타그램 링크 기반 분석 시스템의 입력 규칙, 데이터 파싱 규칙,
그리고 리포트에 반드시 포함되어야 할 인사이트 항목을 정의한다. 실행 계층
(`execution/`)의 스크립트는 이 지침을 기준으로 동작한다.

---

## 1. 아키텍처 개요 (3계층)

| 계층 | 위치 | 역할 |
| --- | --- | --- |
| Directive | `directives/` | 입력·파싱·리포트 규칙 정의(본 문서) |
| Execution | `execution/` | 스크래핑 → 분석 → 리포트 생성 스크립트 |
| 임시/산출물 | `.tmp/`, `Output/` | 중간 데이터 및 최종 리포트 |

**데이터 흐름**

```
URL → instagram_scraper → .tmp/raw_data.json
    → content_analyzer  → .tmp/analysis.json
    → report_generator  → Output/analysis_report.md
```

`run_analysis.py`가 위 3단계를 한 번에 실행하는 메인 엔트리 포인트다.

```bash
python execution/run_analysis.py "https://www.instagram.com/{username}/"
```

---

## 2. 입력 가능한 URL 형식

| 유형 | 예시 | 스크래퍼 입력 방식 |
| --- | --- | --- |
| 프로필 | `https://www.instagram.com/{username}/` | `username` 배열 |
| 포스트 | `https://www.instagram.com/p/{shortcode}/` | `directUrls` |
| 릴스 | `https://www.instagram.com/reel/{shortcode}/` | `directUrls` |
| 프로필 내 포스트 | `https://www.instagram.com/{username}/p/{shortcode}/` | `directUrls` |

규칙:
- `instagram.com` 도메인만 허용한다. 그 외 도메인은 거부한다.
- 사용자명은 영문/숫자/마침표/언더스코어(`[A-Za-z0-9._]`)만 허용한다.
- `explore`, `stories`, `tv` 등 예약 경로는 지원하지 않는다.
- 프로필 URL은 최신 포스트를 `RESULTS_LIMIT`개까지 수집한다(기본 30).

> 참고: 기본 액터는 `apify/instagram-post-scraper`다. 직접 포스트 URL
> (`directUrls`) 처리가 필요하면 환경변수 `APIFY_ACTOR_ID`를
> `apify/instagram-scraper` 등 호환 액터로 변경한다.

---

## 3. 데이터 파싱 규칙

`content_analyzer.normalize()`는 Apify 원본 항목을 다음 정규화 스키마로 변환한다.
누락 필드는 안전하게 `None`/빈 값으로 처리한다.

| 정규화 필드 | 원본 키 | 비고 |
| --- | --- | --- |
| `url` | `url` / `postUrl` | 포스트 영구 링크 |
| `type` | `type` / `productType` | Image / Video / Sidecar |
| `caption` | `caption` | 본문 텍스트 |
| `alt` | `alt` | 이미지 자동 설명(비주얼 분석 단서) |
| `hashtags` | `hashtags` | 해시태그 배열 |
| `mentions` | `mentions` | 멘션 배열 |
| `likes` | `likesCount` | 정수 변환 |
| `comments` | `commentsCount` | 정수 변환 |
| `video_views` | `videoViewCount` | 영상 조회수 |
| `timestamp` | `timestamp` | 게시 시각(ISO) |
| `top_comments` | `latestComments[].text` | 상위 3개 |

집계 통계(`compute_stats`)는 Python에서 결정론적으로 계산한다:
좋아요/댓글의 평균·중앙값·최대·최소, 콘텐츠 유형 분포, 포스트당 평균 해시태그,
상위 해시태그 빈도(15개), 좋아요 상위 포스트(5개).

> 정성 분석은 Gemini가 담당하고, 정량 통계는 Python이 담당한다. 이 분리로
> 수치는 항상 재현 가능하며, LLM 호출이 실패해도 통계 기반 리포트가 생성된다.

---

## 4. 리포트 필수 인사이트 항목

`Output/analysis_report.md`에는 아래 항목이 반드시 포함되어야 한다.
모든 서술은 **한국어**, **전문 마케팅 분석가 톤**으로 작성한다.

1. **핵심 요약** — 계정 전반에 대한 3~4문장 요약.
2. **참여 지표 스냅샷** — 좋아요/댓글 통계 표, 콘텐츠 유형 분포, 상위 해시태그,
   좋아요 상위 포스트 표.
3. **비주얼 컨셉** — 반복되는 시각 테마, 색감·무드.
4. **텍스트 톤앤매너** — 보이스, 핵심 키워드, 어조.
5. **해시태그 전략** — 사용 패턴, 브랜드 전용 태그, 도달 전략.
6. **사용자 반응** — 공감 포인트, 참여를 끌어내는 시그널.
7. **타깃 페르소나** — 인구통계 추정, 관심사, 가치관.
8. **추천 전략** — 우선순위(높음/중간/낮음)가 부여된 실행 가능한 제안.
9. **주목할 포스트** — 벤치마킹할 만한 대표 포스트와 그 이유.

작성 원칙:
- 데이터에 근거하고, 추론이 필요한 부분은 추론임을 드러낸다(과장 금지).
- 수치는 통계 블록의 값을 사용하고 임의로 지어내지 않는다.
- 비주얼 분석은 캡션·이미지 자동 설명(`alt`)·콘텐츠 유형을 단서로 추정한다.

---

## 5. 환경변수

`.env`(루트, 커밋 금지)에 다음을 설정한다.

| 변수 | 필수 | 설명 |
| --- | --- | --- |
| `APIFY_API_TOKEN` | ✅ | Apify API 토큰 |
| `GEMINI_API_KEY` | ✅ | Google Gemini API 키 |
| `APIFY_ACTOR_ID` | – | 기본 `apify/instagram-post-scraper` |
| `GEMINI_MODEL` | – | 기본 `gemini-2.5-flash` |
| `RESULTS_LIMIT` | – | 기본 `30` |

`GEMINI_API_KEY`가 없으면 정성 분석은 생략되고 통계 기반 폴백 리포트가 생성된다.
