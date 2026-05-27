"""Execution Layer 1 — Instagram scraping via Apify.

Takes an Instagram profile/post/reel URL, runs the Apify "Instagram Post
Scraper", and writes the raw items to ``.tmp/raw_data.json``.

    python execution/instagram_scraper.py "https://www.instagram.com/nike/"
"""

from __future__ import annotations

import argparse
import json
import logging
import re
import sys
from dataclasses import dataclass
from urllib.parse import urlparse

from config import (
    APIFY_ACTOR_ID,
    APIFY_API_TOKEN,
    RAW_DATA_PATH,
    RESULTS_LIMIT,
    ensure_dirs,
)

logging.basicConfig(level=logging.INFO, format="%(levelname)s %(message)s")
logger = logging.getLogger("instagram_scraper")

# First path segment values that are NOT usernames.
_RESERVED = {"p", "reel", "reels", "tv", "explore", "stories", "s"}


@dataclass
class ParsedUrl:
    kind: str  # "profile" | "post" | "reel"
    username: str | None
    shortcode: str | None
    url: str


def parse_instagram_url(url: str) -> ParsedUrl:
    """Classify an Instagram URL and pull out username/shortcode."""
    url = url.strip()
    parsed = urlparse(url if "://" in url else f"https://{url}")
    host = parsed.netloc.lower()
    if "instagram.com" not in host:
        raise ValueError(f"인스타그램 URL이 아닙니다: {url}")

    segments = [s for s in parsed.path.split("/") if s]
    if not segments:
        raise ValueError("프로필 또는 포스트 경로가 없습니다.")

    first = segments[0].lower()
    if first in {"p", "reel", "reels", "tv"}:
        if len(segments) < 2:
            raise ValueError("포스트 shortcode를 찾을 수 없습니다.")
        kind = "reel" if first in {"reel", "reels"} else "post"
        return ParsedUrl(kind=kind, username=None, shortcode=segments[1], url=url)

    if first in _RESERVED:
        raise ValueError(f"지원하지 않는 URL 유형입니다: /{first}/")

    username = segments[0]
    if not re.fullmatch(r"[A-Za-z0-9._]+", username):
        raise ValueError(f"올바른 사용자명이 아닙니다: {username}")
    # A profile URL may still embed a specific post: /{user}/p/{shortcode}/
    if len(segments) >= 3 and segments[1].lower() in {"p", "reel", "reels"}:
        return ParsedUrl(
            kind="post", username=username, shortcode=segments[2], url=url
        )
    return ParsedUrl(kind="profile", username=username, shortcode=None, url=url)


def build_actor_input(target: ParsedUrl, limit: int) -> dict:
    """Build input for the Apify Instagram Post Scraper.

    Profile URLs are queried by username; direct post/reel URLs are passed
    through ``directUrls``. If your actor variant doesn't support one of these
    fields, set APIFY_ACTOR_ID to a compatible actor (e.g. apify/instagram-scraper).
    """
    if target.kind == "profile":
        return {"username": [target.username], "resultsLimit": limit}
    return {"directUrls": [target.url], "resultsLimit": limit}


def scrape(url: str, limit: int = RESULTS_LIMIT, token: str | None = None) -> list[dict]:
    token = (token or APIFY_API_TOKEN).strip()
    if not token:
        raise RuntimeError(
            "APIFY_API_TOKEN이 설정되지 않았습니다. .env에 토큰을 추가하세요."
        )

    target = parse_instagram_url(url)
    run_input = build_actor_input(target, limit)
    logger.info(
        "Apify 액터 실행: actor=%s, 유형=%s, 입력=%s",
        APIFY_ACTOR_ID,
        target.kind,
        run_input,
    )

    from apify_client import ApifyClient

    client = ApifyClient(token)
    run = client.actor(APIFY_ACTOR_ID).call(run_input=run_input)
    if not run or not run.get("defaultDatasetId"):
        raise RuntimeError(f"Apify 액터 실행 결과가 비어 있습니다: {run}")

    items = list(client.dataset(run["defaultDatasetId"]).iterate_items())
    logger.info("수집된 항목 수: %d", len(items))
    return items


def save(items: list[dict]) -> None:
    ensure_dirs()
    RAW_DATA_PATH.write_text(
        json.dumps(items, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    logger.info("원본 데이터 저장: %s", RAW_DATA_PATH)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Instagram URL을 Apify로 스크랩합니다.")
    parser.add_argument("url", help="인스타그램 프로필/포스트/릴 URL")
    parser.add_argument(
        "--limit", type=int, default=RESULTS_LIMIT, help="수집할 최대 포스트 수"
    )
    args = parser.parse_args(argv)

    try:
        items = scrape(args.url, limit=args.limit)
    except Exception as exc:
        logger.error("스크래핑 실패: %s", exc)
        return 1

    if not items:
        logger.warning("수집된 포스트가 없습니다. URL 또는 액터 입력을 확인하세요.")
    save(items)
    return 0


if __name__ == "__main__":
    sys.exit(main())
