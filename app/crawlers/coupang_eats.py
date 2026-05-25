from __future__ import annotations

from playwright.async_api import ElementHandle

from app.crawlers.base import BaseCrawler
from app.models import Platform, Review

REVIEW_ITEM = ["li.review-item", "div.reviews li", "article.review"]
TEXT = ["p.review-comment", "div.comment", "p.content"]
AUTHOR = ["span.reviewer", "span.author", "strong.name"]
DATE = ["span.review-date", "time"]
RATING = ["div.rating", "span.star", "div.score"]
MORE_TEXT_BTN = ["button.more", "span.more"]


class CoupangEatsCrawler(BaseCrawler):
    """Coupang Eats requires a logged-in session (see scripts/save_login.py)."""

    platform = Platform.COUPANG_EATS
    url_hosts = ("coupangeats.com", "eats.coupang.com", "store.coupangeats.com")

    async def open(self, url: str) -> None:
        await self.page.goto(url, wait_until="domcontentloaded", timeout=30000)
        await self.page.wait_for_timeout(2000)

    async def review_elements(self) -> list[ElementHandle]:
        for sel in REVIEW_ITEM:
            els = await self.page.query_selector_all(sel)
            if els:
                return els
        return []

    async def load_more(self) -> bool:
        clicked = await self.click_if_present("button.more-review")
        await self.scroll_step()
        return clicked or True

    async def parse(self, el: ElementHandle) -> Review:
        await self.expand_truncated(el, MORE_TEXT_BTN)
        text = await self.text_of(el, TEXT)
        author = await self.text_of(el, AUTHOR)
        date = await self.text_of(el, DATE)
        rating_raw = await self.text_of(el, RATING)
        return Review(
            platform=self.platform,
            text=text,
            author=author or None,
            date=date or None,
            rating=self.parse_rating(rating_raw),
        )
