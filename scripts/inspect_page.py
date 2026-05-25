"""Inspect a live review page to tune selectors accurately.

Run this locally (where the target site is reachable) on a real URL. It uses
the platform crawler's own navigation logic, then reports:
  - how many elements each candidate selector currently matches
  - the outerHTML of the first detected review item (for picking selectors)
  - a saved full page HTML + screenshot under data/output/

Share the printed output (especially the first-item HTML) and selectors can be
locked in exactly for that platform.

Usage:
    python scripts/inspect_page.py naver  "https://m.place.naver.com/restaurant/<id>/review/visitor"
    python scripts/inspect_page.py kakao  "<url>"   --show          # headed browser
"""

from __future__ import annotations

import argparse
import asyncio
import importlib
import sys
from pathlib import Path

sys.path.insert(0, ".")

from playwright.async_api import async_playwright  # noqa: E402

from app.config import BASE_DIR, settings  # noqa: E402
from app.crawlers.registry import CRAWLERS  # noqa: E402
from app.models import Platform  # noqa: E402

# Module-level selector lists each crawler defines; reported as match counts.
SELECTOR_GROUPS = ["REVIEW_ITEM", "TEXT", "AUTHOR", "DATE", "RATING", "MORE_TEXT_BTN"]

MOBILE_UA = (
    "Mozilla/5.0 (iPhone; CPU iPhone OS 16_0 like Mac OS X) "
    "AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.0 Mobile/15E148 Safari/604.1"
)


def crawler_class(platform: Platform):
    for c in CRAWLERS:
        if c.platform == platform:
            return c
    raise SystemExit(f"no crawler for {platform}")


def selector_module(platform: Platform):
    return importlib.import_module(f"app.crawlers.{platform.value}")


async def count_matches(page, selectors: list[str]) -> list[tuple[str, int]]:
    out = []
    for sel in selectors:
        try:
            n = len(await page.query_selector_all(sel))
        except Exception as exc:
            n = -1  # invalid selector
        out.append((sel, n))
    return out


async def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("platform", choices=[p.value for p in Platform])
    parser.add_argument("url")
    parser.add_argument("--show", action="store_true", help="run a visible browser")
    parser.add_argument("--scrolls", type=int, default=5)
    args = parser.parse_args()

    platform = Platform(args.platform)
    cls = crawler_class(platform)
    mod = selector_module(platform)

    out_dir = BASE_DIR / "data" / "output"
    out_dir.mkdir(parents=True, exist_ok=True)

    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=not args.show)
        context = await browser.new_context(
            user_agent=MOBILE_UA,
            viewport={"width": 414, "height": 896},
            locale="ko-KR",
        )
        page = await context.new_page()
        crawler = cls(page, settings.screenshot_path)

        print(f"\n[{platform.label}] opening {args.url}")
        await crawler.open(args.url)
        for _ in range(args.scrolls):
            await crawler.load_more()
            await asyncio.sleep(0.7)

        print("\n=== 셀렉터 매칭 결과 (개수, -1=잘못된 셀렉터) ===")
        for group in SELECTOR_GROUPS:
            selectors = getattr(mod, group, None)
            if not selectors:
                continue
            print(f"\n[{group}]")
            for sel, n in await count_matches(page, selectors):
                mark = "<-- 매칭" if n > 0 else ""
                print(f"  {n:>4}  {sel}  {mark}")

        els = await crawler.review_elements()
        print(f"\n=== review_elements() 가 찾은 리뷰 항목: {len(els)}개 ===")

        html_path = out_dir / f"{platform.value}_page.html"
        html_path.write_text(await page.content(), encoding="utf-8")
        shot_path = out_dir / f"{platform.value}_page.png"
        await page.screenshot(path=str(shot_path), full_page=True)
        print(f"전체 페이지 HTML 저장: {html_path}")
        print(f"전체 페이지 스크린샷 저장: {shot_path}")

        if els:
            first_html = await els[0].evaluate("el => el.outerHTML")
            item_path = out_dir / f"{platform.value}_first_item.html"
            item_path.write_text(first_html, encoding="utf-8")
            print(f"\n첫 번째 리뷰 항목 HTML 저장: {item_path}")
            print("\n=== 첫 번째 리뷰 항목 HTML (앞부분) ===")
            print(first_html[:2000])
        else:
            print(
                "\n[주의] 알려진 셀렉터로 리뷰 항목을 못 찾았습니다. "
                f"{html_path} 를 열어 실제 리뷰 항목의 태그/클래스를 확인해 공유해주세요."
            )

        await browser.close()


if __name__ == "__main__":
    asyncio.run(main())
