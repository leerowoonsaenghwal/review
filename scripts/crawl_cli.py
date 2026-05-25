"""Crawl a review URL from the command line (useful for quick testing).

Usage:
    python scripts/crawl_cli.py https://m.place.naver.com/restaurant/12345/review/visitor
    python scripts/crawl_cli.py <url> --limit 30 --no-analyze
"""

from __future__ import annotations

import argparse
import asyncio
import sys

sys.path.insert(0, ".")

from app.crawl_service import crawl_reviews  # noqa: E402


async def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("url")
    parser.add_argument("--limit", type=int, default=None)
    parser.add_argument("--no-analyze", action="store_true")
    args = parser.parse_args()

    reviews = await crawl_reviews(
        args.url, limit=args.limit, analyze=not args.no_analyze
    )
    print(f"\n총 {len(reviews)}개 리뷰\n" + "=" * 50)
    for r in reviews:
        score = f" ★{r.rating}" if r.rating else ""
        print(f"[{r.sentiment.label}]{score} {r.author or '익명'} ({r.date or '-'})")
        print(f"  {r.text[:120]}")
        if r.screenshot:
            print(f"  📷 {r.screenshot}")
        print("-" * 50)


if __name__ == "__main__":
    asyncio.run(main())
