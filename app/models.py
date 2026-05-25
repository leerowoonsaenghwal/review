from __future__ import annotations

import hashlib
from datetime import datetime
from enum import Enum
from typing import Optional

from pydantic import BaseModel, Field


class Platform(str, Enum):
    NAVER = "naver"
    KAKAO = "kakao"
    BAEMIN = "baemin"
    COUPANG_EATS = "coupang_eats"

    @property
    def label(self) -> str:
        return {
            Platform.NAVER: "네이버 플레이스",
            Platform.KAKAO: "카카오맵",
            Platform.BAEMIN: "배달의민족",
            Platform.COUPANG_EATS: "쿠팡이츠",
        }[self]

    @property
    def requires_login(self) -> bool:
        return self in {Platform.BAEMIN, Platform.COUPANG_EATS}


class Sentiment(str, Enum):
    POSITIVE = "positive"
    NEGATIVE = "negative"
    NEUTRAL = "neutral"
    UNKNOWN = "unknown"

    @property
    def label(self) -> str:
        return {
            Sentiment.POSITIVE: "긍정",
            Sentiment.NEGATIVE: "부정",
            Sentiment.NEUTRAL: "중립",
            Sentiment.UNKNOWN: "미분류",
        }[self]


class Review(BaseModel):
    """A single crawled review."""

    platform: Platform
    author: Optional[str] = None
    rating: Optional[float] = None
    text: str = ""
    date: Optional[str] = None
    # Path to the per-review screenshot, relative to the screenshot dir.
    screenshot: Optional[str] = None

    sentiment: Sentiment = Sentiment.UNKNOWN
    sentiment_confidence: Optional[float] = None
    sentiment_reason: Optional[str] = None

    @property
    def fingerprint(self) -> str:
        """Stable id for de-duplication across re-crawls."""
        key = f"{self.platform.value}|{self.author or ''}|{self.date or ''}|{self.text[:80]}"
        return hashlib.sha1(key.encode("utf-8")).hexdigest()[:16]


class JobStatus(str, Enum):
    PENDING = "pending"
    RUNNING = "running"
    DONE = "done"
    ERROR = "error"


class CrawlJob(BaseModel):
    id: str
    url: str
    platform: Optional[Platform] = None
    status: JobStatus = JobStatus.PENDING
    message: str = ""
    created_at: datetime = Field(default_factory=datetime.utcnow)
    reviews: list[Review] = Field(default_factory=list)

    @property
    def positive(self) -> list[Review]:
        return [r for r in self.reviews if r.sentiment == Sentiment.POSITIVE]

    @property
    def negative(self) -> list[Review]:
        return [r for r in self.reviews if r.sentiment == Sentiment.NEGATIVE]

    @property
    def other(self) -> list[Review]:
        return [
            r
            for r in self.reviews
            if r.sentiment in (Sentiment.NEUTRAL, Sentiment.UNKNOWN)
        ]
