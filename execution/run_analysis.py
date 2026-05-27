"""Main entry point — end-to-end Instagram analysis.

    python execution/run_analysis.py "https://www.instagram.com/nike/"

Runs the full pipeline: scrape → analyze → report. Use --skip-scrape to reuse
the existing .tmp/raw_data.json (e.g. when iterating on the report format).
"""

from __future__ import annotations

import argparse
import logging
import sys

import content_analyzer
import instagram_scraper
import report_generator
from config import REPORT_PATH, RESULTS_LIMIT

logging.basicConfig(level=logging.INFO, format="%(levelname)s %(message)s")
logger = logging.getLogger("run_analysis")


def run(url: str, limit: int, skip_scrape: bool) -> int:
    if skip_scrape:
        logger.info("[1/3] 스크래핑 건너뜀 — 기존 raw_data.json 사용")
    else:
        logger.info("[1/3] 인스타그램 스크래핑: %s", url)
        items = instagram_scraper.scrape(url, limit=limit)
        if not items:
            logger.error("수집된 포스트가 없습니다. 중단합니다.")
            return 1
        instagram_scraper.save(items)

    logger.info("[2/3] 콘텐츠 분석 (Gemini)")
    analysis = content_analyzer.analyze()
    content_analyzer.save(analysis)

    logger.info("[3/3] 리포트 생성")
    report_generator.generate()

    logger.info("✅ 완료 — 리포트: %s", REPORT_PATH)
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="인스타그램 URL을 분석해 마케팅 리포트를 생성합니다."
    )
    parser.add_argument("url", help="인스타그램 프로필/포스트/릴 URL")
    parser.add_argument(
        "--limit", type=int, default=RESULTS_LIMIT, help="수집할 최대 포스트 수"
    )
    parser.add_argument(
        "--skip-scrape",
        action="store_true",
        help="스크래핑을 건너뛰고 기존 .tmp/raw_data.json 재사용",
    )
    args = parser.parse_args(argv)

    try:
        return run(args.url, args.limit, args.skip_scrape)
    except Exception as exc:
        logger.error("파이프라인 실패: %s", exc)
        return 1


if __name__ == "__main__":
    sys.exit(main())
