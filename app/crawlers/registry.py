from __future__ import annotations

from app.crawlers.base import BaseCrawler
from app.crawlers.baemin import BaeminCrawler
from app.crawlers.coupang_eats import CoupangEatsCrawler
from app.crawlers.kakao import KakaoCrawler
from app.crawlers.naver import NaverCrawler
from app.models import Platform

CRAWLERS: list[type[BaseCrawler]] = [
    NaverCrawler,
    KakaoCrawler,
    BaeminCrawler,
    CoupangEatsCrawler,
]


def crawler_for_url(url: str) -> type[BaseCrawler] | None:
    for crawler in CRAWLERS:
        if crawler.matches(url):
            return crawler
    return None


def platform_for_url(url: str) -> Platform | None:
    crawler = crawler_for_url(url)
    return crawler.platform if crawler else None
