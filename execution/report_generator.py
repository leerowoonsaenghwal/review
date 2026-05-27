"""Execution Layer 3 — premium Markdown report.

Reads ``.tmp/analysis.json`` and renders ``Output/analysis_report.md`` in
Korean, with a professional marketing-analyst tone.
"""

from __future__ import annotations

import json
import logging
import sys
from datetime import datetime

from config import ANALYSIS_PATH, REPORT_PATH, ensure_dirs

logging.basicConfig(level=logging.INFO, format="%(levelname)s %(message)s")
logger = logging.getLogger("report_generator")

_PRIORITY_LABEL = {"high": "높음", "medium": "중간", "low": "낮음"}


def load_analysis() -> dict:
    if not ANALYSIS_PATH.exists():
        raise FileNotFoundError(
            f"분석 데이터가 없습니다: {ANALYSIS_PATH} (먼저 분석기를 실행하세요)"
        )
    return json.loads(ANALYSIS_PATH.read_text(encoding="utf-8"))


def render(analysis: dict) -> str:
    account = analysis.get("account", {})
    stats = analysis.get("stats", {})
    handle = account.get("username") or account.get("full_name") or "분석 대상 계정"
    lines: list[str] = []

    # --- Header --------------------------------------------------------
    lines.append(f"# 📊 인스타그램 분석 리포트 — @{handle}")
    lines.append("")
    lines.append("> 본 리포트는 수집된 포스트 데이터를 기반으로 자동 생성된 마케팅 분석 자료입니다.")
    lines.append("")
    full_name = account.get("full_name")
    meta = [
        f"- **계정**: @{account.get('username', '-')}"
        + (f" ({full_name})" if full_name else ""),
        f"- **분석 포스트 수**: {account.get('post_count', 0)}건",
        f"- **분석 모델**: {analysis.get('model', '-')}",
        f"- **생성 시각**: {_fmt_ts(analysis.get('generated_at'))}",
    ]
    lines.extend(meta)
    lines.append("")

    # --- Executive summary --------------------------------------------
    if analysis.get("summary"):
        lines.append("## 📌 핵심 요약")
        lines.append("")
        lines.append(analysis["summary"])
        lines.append("")

    # --- Engagement snapshot ------------------------------------------
    lines.extend(_render_stats(stats))

    # --- Qualitative sections -----------------------------------------
    lines.extend(
        _render_kv_section(
            "🎨 비주얼 컨셉",
            analysis.get("visual_concept", {}),
            {"overview": "개요", "themes": "주요 테마", "color_mood": "색감·무드"},
        )
    )
    lines.extend(
        _render_kv_section(
            "✍️ 텍스트 톤앤매너",
            analysis.get("tone_and_manner", {}),
            {"overview": "개요", "keywords": "핵심 키워드", "voice": "보이스"},
        )
    )
    lines.extend(
        _render_kv_section(
            "#️⃣ 해시태그 전략",
            analysis.get("hashtag_strategy", {}),
            {"overview": "개요", "patterns": "패턴", "branded_tags": "브랜드 태그"},
        )
    )
    lines.extend(
        _render_kv_section(
            "💬 사용자 반응",
            analysis.get("audience_response", {}),
            {
                "overview": "개요",
                "what_resonates": "공감 포인트",
                "signals": "주요 시그널",
            },
        )
    )
    lines.extend(
        _render_kv_section(
            "🧑 타깃 페르소나",
            analysis.get("persona", {}),
            {
                "summary": "요약",
                "demographics": "인구통계 추정",
                "interests": "관심사",
                "values": "가치관",
            },
        )
    )

    # --- Recommendations ----------------------------------------------
    recs = analysis.get("recommendations") or []
    if recs:
        lines.append("## 🚀 추천 전략")
        lines.append("")
        for i, rec in enumerate(recs, 1):
            if not isinstance(rec, dict):
                continue
            prio = _PRIORITY_LABEL.get(str(rec.get("priority", "")).lower())
            badge = f" `우선순위: {prio}`" if prio else ""
            lines.append(f"{i}. **{rec.get('title', '제안')}**{badge}")
            if rec.get("detail"):
                lines.append(f"   - {rec['detail']}")
        lines.append("")

    # --- Highlights ----------------------------------------------------
    highlights = analysis.get("post_highlights") or []
    if highlights:
        lines.append("## ⭐ 주목할 포스트")
        lines.append("")
        for h in highlights:
            if not isinstance(h, dict):
                continue
            ref = h.get("reference", "")
            lines.append(f"- **{ref}** — {h.get('why', '')}")
        lines.append("")

    lines.append("---")
    lines.append("")
    lines.append("_본 리포트는 자동 분석 도구로 생성되었으며, 정성 분석은 데이터 기반 추론을 포함합니다._")
    lines.append("")
    return "\n".join(lines)


def _render_stats(stats: dict) -> list[str]:
    if not stats:
        return []
    likes = stats.get("likes", {})
    comments = stats.get("comments", {})
    out = ["## 📈 참여 지표 스냅샷", ""]
    out.append("| 지표 | 평균 | 중앙값 | 최대 | 최소 |")
    out.append("| --- | ---: | ---: | ---: | ---: |")
    out.append(
        f"| 좋아요 | {likes.get('avg', 0)} | {likes.get('median', 0)} | "
        f"{likes.get('max', 0)} | {likes.get('min', 0)} |"
    )
    out.append(
        f"| 댓글 | {comments.get('avg', 0)} | {comments.get('median', 0)} | "
        f"{comments.get('max', 0)} | {comments.get('min', 0)} |"
    )
    out.append("")

    type_breakdown = stats.get("type_breakdown") or {}
    if type_breakdown:
        types = ", ".join(f"{k} {v}건" for k, v in type_breakdown.items())
        out.append(f"- **콘텐츠 유형 분포**: {types}")
    out.append(f"- **포스트당 평균 해시태그**: {stats.get('avg_hashtags_per_post', 0)}개")

    top_tags = stats.get("top_hashtags") or []
    if top_tags:
        tag_str = " ".join(f"`#{t}`({n})" for t, n in top_tags[:12])
        out.append(f"- **상위 해시태그**: {tag_str}")
    out.append("")

    top_posts = stats.get("top_posts") or []
    if top_posts:
        out.append("### 좋아요 상위 포스트")
        out.append("")
        out.append("| 좋아요 | 댓글 | 캡션 |")
        out.append("| ---: | ---: | --- |")
        for p in top_posts:
            cap = (p.get("caption_excerpt") or "").replace("|", "/").replace("\n", " ")
            url = p.get("url")
            cap_cell = f"[{cap}]({url})" if url and cap else (cap or (url or "-"))
            out.append(f"| {p.get('likes', 0)} | {p.get('comments', 0)} | {cap_cell} |")
        out.append("")
    return out


def _render_kv_section(title: str, data: dict, field_labels: dict) -> list[str]:
    if not data:
        return []
    out = [f"## {title}", ""]
    for key, label in field_labels.items():
        value = data.get(key)
        if not value:
            continue
        if isinstance(value, list):
            items = [str(v) for v in value if str(v).strip()]
            if not items:
                continue
            out.append(f"**{label}**")
            out.extend(f"- {v}" for v in items)
            out.append("")
        else:
            out.append(f"**{label}**: {value}")
            out.append("")
    if len(out) == 2:  # title only, no content rendered
        return []
    return out


def _fmt_ts(value) -> str:
    if not value:
        return datetime.now().strftime("%Y-%m-%d %H:%M")
    try:
        return datetime.fromisoformat(value).strftime("%Y-%m-%d %H:%M UTC")
    except (ValueError, TypeError):
        return str(value)


def generate() -> str:
    analysis = load_analysis()
    report = render(analysis)
    ensure_dirs()
    REPORT_PATH.write_text(report, encoding="utf-8")
    logger.info("리포트 생성 완료: %s", REPORT_PATH)
    return report


def main() -> int:
    try:
        generate()
    except Exception as exc:
        logger.error("리포트 생성 실패: %s", exc)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
