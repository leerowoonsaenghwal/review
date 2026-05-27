"""Execution Layer 2 — content analysis via Gemini.

Reads the scraped posts from ``.tmp/raw_data.json``, computes deterministic
engagement statistics, then asks Gemini for a qualitative read (visual concept,
tone & manner, hashtag strategy, audience response, persona, recommendations).
The merged result is written to ``.tmp/analysis.json``.

If no GEMINI_API_KEY is configured (or the call fails), it falls back to a
statistics-only analysis so the pipeline still produces a report.
"""

from __future__ import annotations

import json
import logging
import statistics
import sys
from collections import Counter
from datetime import datetime, timezone

from config import (
    ANALYSIS_PATH,
    GEMINI_API_KEY,
    GEMINI_MODEL,
    RAW_DATA_PATH,
    ensure_dirs,
)

logging.basicConfig(level=logging.INFO, format="%(levelname)s %(message)s")
logger = logging.getLogger("content_analyzer")

SYSTEM_PROMPT = (
    "당신은 인스타그램 계정을 분석하는 전문 소셜미디어 마케팅 분석가입니다. "
    "제공된 포스트 데이터(캡션, 해시태그, 이미지 설명, 참여 지표)를 근거로 "
    "비주얼 컨셉, 텍스트 톤앤매너, 해시태그 전략, 사용자 반응, 타깃 페르소나를 "
    "심층 분석하고 실행 가능한 전략을 제안하세요. "
    "추측이 필요한 경우 데이터 기반 추론임을 드러내고, 과장 없이 전문적으로 작성합니다. "
    "모든 내용은 한국어로 작성하세요."
)

# The exact JSON shape the report generator expects back from the model.
RESPONSE_SCHEMA_HINT = """
반드시 아래 JSON 구조로만 응답하세요 (모든 값은 한국어):
{
  "summary": "계정 전반에 대한 3~4문장 요약",
  "visual_concept": {"overview": "...", "themes": ["..."], "color_mood": "..."},
  "tone_and_manner": {"overview": "...", "keywords": ["..."], "voice": "..."},
  "hashtag_strategy": {"overview": "...", "patterns": ["..."], "branded_tags": ["..."]},
  "audience_response": {"overview": "...", "what_resonates": ["..."], "signals": ["..."]},
  "persona": {"summary": "...", "demographics": "...", "interests": ["..."], "values": ["..."]},
  "recommendations": [{"title": "...", "detail": "...", "priority": "high|medium|low"}],
  "post_highlights": [{"reference": "포스트 URL 또는 캡션 일부", "why": "주목할 이유"}]
}
"""


def load_raw() -> list[dict]:
    if not RAW_DATA_PATH.exists():
        raise FileNotFoundError(
            f"원본 데이터가 없습니다: {RAW_DATA_PATH} (먼저 스크래퍼를 실행하세요)"
        )
    data = json.loads(RAW_DATA_PATH.read_text(encoding="utf-8"))
    if not isinstance(data, list):
        raise ValueError("raw_data.json 형식이 올바르지 않습니다 (리스트 기대).")
    return data


def normalize(items: list[dict]) -> tuple[dict, list[dict]]:
    """Return (account_info, normalized_posts)."""
    posts: list[dict] = []
    for it in items:
        posts.append(
            {
                "url": it.get("url") or it.get("postUrl"),
                "type": it.get("type") or it.get("productType") or "Unknown",
                "caption": (it.get("caption") or "").strip(),
                "alt": (it.get("alt") or "").strip(),
                "hashtags": it.get("hashtags") or [],
                "mentions": it.get("mentions") or [],
                "likes": _count(it.get("likesCount")),
                "comments": _count(it.get("commentsCount")),
                "video_views": _count(it.get("videoViewCount")),
                "timestamp": it.get("timestamp"),
                "top_comments": [
                    c.get("text", "")
                    for c in (it.get("latestComments") or [])[:3]
                    if isinstance(c, dict)
                ],
            }
        )

    owner = next(
        (
            it
            for it in items
            if it.get("ownerUsername") or it.get("ownerFullName")
        ),
        {},
    )
    account = {
        "username": owner.get("ownerUsername"),
        "full_name": owner.get("ownerFullName"),
        "post_count": len(posts),
    }
    return account, posts


def compute_stats(posts: list[dict]) -> dict:
    likes = [p["likes"] for p in posts if p["likes"] is not None]
    comments = [p["comments"] for p in posts if p["comments"] is not None]
    type_counts = Counter(p["type"] for p in posts)

    tag_counter: Counter[str] = Counter()
    for p in posts:
        tag_counter.update(t.lower() for t in p["hashtags"])

    top_by_likes = sorted(
        (p for p in posts if p["likes"] is not None),
        key=lambda p: p["likes"],
        reverse=True,
    )[:5]

    return {
        "post_count": len(posts),
        "type_breakdown": dict(type_counts),
        "likes": _metric_block(likes),
        "comments": _metric_block(comments),
        "avg_hashtags_per_post": round(
            statistics.mean([len(p["hashtags"]) for p in posts]), 1
        )
        if posts
        else 0,
        "top_hashtags": tag_counter.most_common(15),
        "top_posts": [
            {
                "url": p["url"],
                "likes": p["likes"],
                "comments": p["comments"],
                "caption_excerpt": p["caption"][:80],
            }
            for p in top_by_likes
        ],
    }


def _metric_block(values: list[int]) -> dict:
    if not values:
        return {"count": 0, "avg": 0, "median": 0, "max": 0, "min": 0}
    return {
        "count": len(values),
        "avg": round(statistics.mean(values), 1),
        "median": round(statistics.median(values), 1),
        "max": max(values),
        "min": min(values),
    }


def _as_int(value) -> int | None:
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def _count(value) -> int | None:
    """Engagement count, or None when hidden (Instagram returns -1)."""
    n = _as_int(value)
    return n if n is not None and n >= 0 else None


def _gemini_available() -> bool:
    return bool(GEMINI_API_KEY)


def _build_prompt(account: dict, stats: dict, posts: list[dict]) -> str:
    compact_posts = [
        {
            "type": p["type"],
            "caption": p["caption"][:600],
            "image_description": p["alt"][:300],
            "hashtags": p["hashtags"][:30],
            "likes": p["likes"],
            "comments": p["comments"],
            "top_comments": p["top_comments"],
        }
        for p in posts
    ]
    return (
        f"## 계정 정보\n{json.dumps(account, ensure_ascii=False)}\n\n"
        f"## 집계 통계\n{json.dumps(stats, ensure_ascii=False)}\n\n"
        f"## 포스트 데이터\n{json.dumps(compact_posts, ensure_ascii=False)}\n\n"
        f"{RESPONSE_SCHEMA_HINT}"
    )


def _qualitative_via_gemini(account: dict, stats: dict, posts: list[dict]) -> dict:
    import google.generativeai as genai

    # REST transport (not gRPC) so the client honors REQUESTS_CA_BUNDLE /
    # SSL_CERT_FILE — required behind TLS-intercepting/corporate proxies.
    genai.configure(api_key=GEMINI_API_KEY, transport="rest")
    model = genai.GenerativeModel(GEMINI_MODEL, system_instruction=SYSTEM_PROMPT)
    resp = model.generate_content(
        _build_prompt(account, stats, posts),
        generation_config={
            "temperature": 0.4,
            "response_mime_type": "application/json",
        },
    )
    return _extract_json(resp.text)


def _extract_json(text: str) -> dict:
    text = (text or "").strip()
    if text.startswith("```"):
        text = text.split("```", 2)[1]
        if text.startswith("json"):
            text = text[4:]
    start, end = text.find("{"), text.rfind("}")
    if start == -1 or end == -1:
        return {}
    try:
        return json.loads(text[start : end + 1])
    except json.JSONDecodeError:
        logger.warning("Gemini 응답을 JSON으로 파싱하지 못했습니다.")
        return {}


def _fallback_qualitative(stats: dict) -> dict:
    return {
        "summary": "GEMINI_API_KEY가 없어 정성 분석을 생략하고 통계 기반 요약만 제공합니다.",
        "visual_concept": {},
        "tone_and_manner": {},
        "hashtag_strategy": {
            "overview": "상위 해시태그 빈도 기준 요약",
            "patterns": [f"#{tag} ({n}회)" for tag, n in stats.get("top_hashtags", [])[:10]],
            "branded_tags": [],
        },
        "audience_response": {
            "overview": f"평균 좋아요 {stats['likes']['avg']}, 평균 댓글 {stats['comments']['avg']}",
        },
        "persona": {},
        "recommendations": [],
        "post_highlights": [],
    }


def analyze() -> dict:
    items = load_raw()
    account, posts = normalize(items)
    if not posts:
        raise ValueError("분석할 포스트가 없습니다.")

    stats = compute_stats(posts)

    if _gemini_available():
        try:
            logger.info("Gemini 정성 분석 실행 (model=%s)", GEMINI_MODEL)
            qualitative = _qualitative_via_gemini(account, stats, posts)
            if not qualitative:
                logger.warning("Gemini 응답이 비어 통계 기반 폴백을 사용합니다.")
                qualitative = _fallback_qualitative(stats)
        except Exception as exc:
            logger.error("Gemini 분석 실패(%s); 통계 기반 폴백 사용", exc)
            qualitative = _fallback_qualitative(stats)
    else:
        logger.warning("GEMINI_API_KEY 미설정 — 통계 기반 폴백 사용")
        qualitative = _fallback_qualitative(stats)

    analysis = {
        "account": account,
        "stats": stats,
        "generated_at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "model": GEMINI_MODEL if _gemini_available() else "fallback(stats-only)",
        **qualitative,
    }
    return analysis


def save(analysis: dict) -> None:
    ensure_dirs()
    ANALYSIS_PATH.write_text(
        json.dumps(analysis, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    logger.info("분석 결과 저장: %s", ANALYSIS_PATH)


def main() -> int:
    try:
        analysis = analyze()
    except Exception as exc:
        logger.error("분석 실패: %s", exc)
        return 1
    save(analysis)
    return 0


if __name__ == "__main__":
    sys.exit(main())
