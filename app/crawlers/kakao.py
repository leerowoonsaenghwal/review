from __future__ import annotations

from playwright.async_api import ElementHandle

from app.crawlers.base import BaseCrawler
from app.models import Platform, Review

REVIEW_ITEM = ["ul.list_review > li", "div.review_evaluation li", "li.Review"]
TEXT = ["p.txt_comment", "p.desc_review", "span.txt_comment"]
AUTHOR = ["span.name_user", "a.link_user", "span.txt_user"]
DATE = ["span.txt_date", "time"]
RATING = ["span.figure_star", "span.screen_out", "div.star_info"]
MORE_TEXT_BTN = ["span.btn_more", "a.link_more", "button.btn_fold"]


class KakaoCrawler(BaseCrawler):
    platform = Platform.KAKAO
    url_hosts = ("place.map.kakao.com", "map.kakao.com", "place.kakao.com")

    async def open(self, url: str) -> None:
        await self.page.goto(url, wait_until="domcontentloaded", timeout=30000)
        await self.page.wait_for_timeout(1500)
        # Kakao place pages render reviews in a panel; nudge to the section.
        await self.click_if_present("a[href='#comment']")
        await self.page.wait_for_timeout(800)

    async def review_elements(self) -> list[ElementHandle]:
        for sel in REVIEW_ITEM:
            els = await self.page.query_selector_all(sel)
            if els:
                return els
        return []

    async def load_more(self) -> bool:
        clicked = await self.click_if_present("a.link_more")  # "후기 더보기"
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
