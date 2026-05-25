from __future__ import annotations

from playwright.async_api import ElementHandle

from app.crawlers.base import BaseCrawler
from app.models import Platform, Review

# Selectors are kept as ordered fallback lists because Naver ships obfuscated,
# frequently-rotated class names. Update these lists when layout changes.
REVIEW_ITEM = ["li.place_apply_pui", "li.pui__X35jYm", "div.place_section_content li"]
TEXT = ["a.pui__xtsQN-", "div.pui__vn15t2", "span.zPfVt"]
AUTHOR = ["span.pui__NMi-Dp", "div.pui__NMi-Dp", "span.P9EZi"]
DATE = ["time.pui__QKE5Pr", "span.pui__blind + span", "time"]
MORE_TEXT_BTN = ["a.pui__wFzIYl", "a.pui__jhpEyP", "span.pui__more"]


class NaverCrawler(BaseCrawler):
    platform = Platform.NAVER
    url_hosts = ("place.naver.com", "m.place.naver.com", "naver.me")

    async def open(self, url: str) -> None:
        # Normalise to the mobile visitor-review tab, which is lighter and
        # has a stable scroll-to-load pattern.
        review_url = self._to_review_url(url)
        await self.page.goto(review_url, wait_until="domcontentloaded", timeout=30000)
        await self.page.wait_for_timeout(1500)

    @staticmethod
    def _to_review_url(url: str) -> str:
        # m.place.naver.com/restaurant/{id}/review/visitor
        import re

        m = re.search(r"/(restaurant|place|hairshop|hospital)/(\d+)", url)
        if m:
            kind, pid = m.group(1), m.group(2)
            return f"https://m.place.naver.com/{kind}/{pid}/review/visitor"
        return url

    async def review_elements(self) -> list[ElementHandle]:
        for sel in REVIEW_ITEM:
            els = await self.page.query_selector_all(sel)
            if els:
                return els
        return []

    async def load_more(self) -> bool:
        # Naver uses a "더보기" button at the list bottom.
        clicked = await self.click_if_present("a.fvwqf")  # "더보기"
        await self.scroll_step()
        return clicked or True

    async def parse(self, el: ElementHandle) -> Review:
        await self.expand_truncated(el, MORE_TEXT_BTN)
        text = await self.text_of(el, TEXT)
        author = await self.text_of(el, AUTHOR)
        date = await self.text_of(el, DATE)
        return Review(
            platform=self.platform,
            text=text,
            author=author or None,
            date=date or None,
            rating=None,  # Naver visitor reviews are text-only (no star score)
        )
