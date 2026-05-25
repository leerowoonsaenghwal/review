from __future__ import annotations

import asyncio
import logging
import re
from abc import ABC, abstractmethod

from playwright.async_api import ElementHandle, Page

from app.config import settings
from app.models import Platform, Review

logger = logging.getLogger(__name__)


class BaseCrawler(ABC):
    """Base class for a single-platform review crawler.

    Subclasses declare the platform, how to recognise a URL, and how to
    locate + parse review elements. The base class owns the generic
    machinery: lazy-load scrolling, "more" clicking, and per-review
    screenshots, so each subclass stays small.
    """

    platform: Platform
    # Host fragments that identify this platform's URLs.
    url_hosts: tuple[str, ...] = ()

    def __init__(self, page: Page, screenshot_dir, limit: int | None = None):
        self.page = page
        self.screenshot_dir = screenshot_dir
        self.limit = limit or settings.max_reviews

    @classmethod
    def matches(cls, url: str) -> bool:
        return any(h in url for h in cls.url_hosts)

    # --- subclass hooks -------------------------------------------------

    @abstractmethod
    async def open(self, url: str) -> None:
        """Navigate to the review section and wait for it to be ready."""

    @abstractmethod
    async def review_elements(self) -> list[ElementHandle]:
        """Return the currently-loaded review container elements."""

    @abstractmethod
    async def parse(self, el: ElementHandle) -> Review:
        """Parse one review container into a Review (no sentiment yet)."""

    async def load_more(self) -> bool:
        """Load additional reviews. Return True if more were (likely) loaded.

        Default implementation scrolls to the bottom. Override to click a
        "더보기"/"more" button when the platform paginates that way.
        """
        return await self.scroll_step()

    # --- generic machinery ----------------------------------------------

    async def crawl(self, url: str) -> list[Review]:
        await self.open(url)
        await self._load_until_enough()

        elements = await self.review_elements()
        elements = elements[: self.limit]
        logger.info("[%s] parsing %d review elements", self.platform.value, len(elements))

        reviews: list[Review] = []
        for idx, el in enumerate(elements):
            try:
                review = await self.parse(el)
                if not review.text.strip():
                    continue
                review.screenshot = await self.capture(el, idx)
                reviews.append(review)
            except Exception as exc:  # one bad element shouldn't kill the run
                logger.warning("[%s] failed to parse element %d: %s",
                               self.platform.value, idx, exc)
        return reviews

    async def _load_until_enough(self, max_rounds: int = 40) -> None:
        seen = -1
        stagnant = 0
        for _ in range(max_rounds):
            count = len(await self.review_elements())
            if count >= self.limit:
                break
            if count == seen:
                stagnant += 1
                if stagnant >= 3:  # nothing new after several tries -> stop
                    break
            else:
                stagnant = 0
            seen = count
            await self.load_more()
            await asyncio.sleep(0.8)

    async def scroll_step(self) -> bool:
        await self.page.mouse.wheel(0, 4000)
        await asyncio.sleep(0.4)
        return True

    async def click_if_present(self, selector: str) -> bool:
        loc = self.page.locator(selector)
        try:
            if await loc.count() and await loc.first.is_visible():
                await loc.first.click(timeout=2000)
                return True
        except Exception:
            pass
        return False

    async def expand_truncated(self, el: ElementHandle, selectors: list[str]) -> None:
        """Click in-review "더보기" links so full text is captured."""
        for sel in selectors:
            try:
                btn = await el.query_selector(sel)
                if btn:
                    await btn.click(timeout=1500)
                    await asyncio.sleep(0.15)
            except Exception:
                pass

    async def capture(self, el: ElementHandle, idx: int) -> str | None:
        name = f"{self.platform.value}_{idx:03d}.png"
        path = self.screenshot_dir / name
        try:
            await el.scroll_into_view_if_needed(timeout=2000)
            await el.screenshot(path=str(path))
            return name
        except Exception as exc:
            logger.warning("[%s] screenshot failed for %d: %s",
                           self.platform.value, idx, exc)
            return None

    @staticmethod
    async def text_of(el: ElementHandle, selectors: list[str]) -> str:
        for sel in selectors:
            node = await el.query_selector(sel)
            if node:
                txt = (await node.inner_text()).strip()
                if txt:
                    return txt
        return ""

    @staticmethod
    def parse_rating(raw: str) -> float | None:
        """Extract a numeric rating from text like '5.0', '별점 4', '★★★★☆'."""
        if not raw:
            return None
        stars = raw.count("★")
        if stars:
            return float(stars)
        m = re.search(r"(\d+(?:\.\d+)?)", raw)
        if m:
            try:
                val = float(m.group(1))
                return val if val <= 5 else None
            except ValueError:
                return None
        return None
