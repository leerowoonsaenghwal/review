from __future__ import annotations

import logging

from playwright.async_api import async_playwright

from app.config import settings
from app.crawlers.registry import crawler_for_url
from app.models import Platform, Review
from app.sentiment import SentimentAnalyzer
from app.session import has_session, storage_state_arg

logger = logging.getLogger(__name__)

MOBILE_UA = (
    "Mozilla/5.0 (iPhone; CPU iPhone OS 16_0 like Mac OS X) "
    "AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.0 Mobile/15E148 Safari/604.1"
)


class LoginRequiredError(RuntimeError):
    def __init__(self, platform: Platform):
        self.platform = platform
        super().__init__(
            f"{platform.label} 크롤링에는 로그인 세션이 필요합니다. "
            f"먼저 `python scripts/save_login.py {platform.value} <URL>` 로 로그인하세요."
        )


class UnsupportedUrlError(RuntimeError):
    pass


async def crawl_reviews(
    url: str, limit: int | None = None, analyze: bool = True
) -> list[Review]:
    """Crawl reviews for a URL, capture screenshots, and classify sentiment."""
    crawler_cls = crawler_for_url(url)
    if crawler_cls is None:
        raise UnsupportedUrlError(
            "지원하지 않는 URL입니다. 네이버/카카오/배민/쿠팡이츠 링크를 입력하세요."
        )

    platform = crawler_cls.platform
    if platform.requires_login and not has_session(platform):
        raise LoginRequiredError(platform)

    storage_state = storage_state_arg(platform)

    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=settings.headless)
        context = await browser.new_context(
            user_agent=MOBILE_UA,
            viewport={"width": 414, "height": 896},
            storage_state=storage_state,
            locale="ko-KR",
        )
        page = await context.new_page()
        crawler = crawler_cls(page, settings.screenshot_path, limit=limit)
        try:
            reviews = await crawler.crawl(url)
        finally:
            await context.close()
            await browser.close()

    reviews = _dedupe(reviews)
    if analyze:
        SentimentAnalyzer().analyze(reviews)
    logger.info("[%s] crawled %d reviews", platform.value, len(reviews))
    return reviews


def _dedupe(reviews: list[Review]) -> list[Review]:
    seen: set[str] = set()
    out: list[Review] = []
    for r in reviews:
        fp = r.fingerprint
        if fp in seen:
            continue
        seen.add(fp)
        out.append(r)
    return out
