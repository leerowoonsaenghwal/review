from __future__ import annotations

import json
import logging

from app.config import settings
from app.models import Review, Sentiment

logger = logging.getLogger(__name__)

SYSTEM_PROMPT = (
    "당신은 한국어 음식점/장소 리뷰의 감정을 분류하는 분석가입니다. "
    "각 리뷰를 positive(긍정), negative(부정), neutral(중립) 중 하나로 분류하세요. "
    "별점이 있으면 참고하되, 본문 내용을 우선합니다. "
    "비꼬는 표현, 반어법, 완곡한 불만을 정확히 잡아내세요.\n"
    "반드시 아래 JSON 형식으로만 응답하세요:\n"
    '{"results": [{"index": 0, "sentiment": "positive|negative|neutral", '
    '"confidence": 0.0~1.0, "reason": "한 줄 근거"}]}'
)

# Process reviews in batches to keep each request bounded.
BATCH_SIZE = 25


class SentimentAnalyzer:
    def __init__(self, api_key: str | None = None, model: str | None = None):
        self.api_key = api_key if api_key is not None else settings.anthropic_api_key
        self.model = model or settings.sentiment_model
        self._client = None

    @property
    def available(self) -> bool:
        return bool(self.api_key)

    def _get_client(self):
        if self._client is None:
            from anthropic import Anthropic

            self._client = Anthropic(api_key=self.api_key)
        return self._client

    def analyze(self, reviews: list[Review]) -> list[Review]:
        """Classify each review in place and return the list."""
        if not reviews:
            return reviews
        if not self.available:
            logger.warning("No ANTHROPIC_API_KEY — falling back to rating-based labels")
            return [self._rating_fallback(r) for r in reviews]

        for start in range(0, len(reviews), BATCH_SIZE):
            batch = reviews[start : start + BATCH_SIZE]
            try:
                self._classify_batch(batch)
            except Exception as exc:
                logger.error("Sentiment batch failed (%s); using rating fallback", exc)
                for r in batch:
                    self._rating_fallback(r)
        return reviews

    def _classify_batch(self, batch: list[Review]) -> None:
        payload = [
            {"index": i, "rating": r.rating, "text": r.text[:1000]}
            for i, r in enumerate(batch)
        ]
        client = self._get_client()
        resp = client.messages.create(
            model=self.model,
            max_tokens=2048,
            system=[
                {
                    "type": "text",
                    "text": SYSTEM_PROMPT,
                    "cache_control": {"type": "ephemeral"},
                }
            ],
            messages=[
                {
                    "role": "user",
                    "content": "다음 리뷰들을 분류하세요:\n"
                    + json.dumps(payload, ensure_ascii=False),
                }
            ],
        )
        text = "".join(b.text for b in resp.content if b.type == "text")
        parsed = self._extract_json(text)
        results = {item["index"]: item for item in parsed.get("results", [])}
        for i, review in enumerate(batch):
            item = results.get(i)
            if not item:
                self._rating_fallback(review)
                continue
            review.sentiment = self._to_sentiment(item.get("sentiment"))
            review.sentiment_confidence = item.get("confidence")
            review.sentiment_reason = item.get("reason")

    @staticmethod
    def _extract_json(text: str) -> dict:
        text = text.strip()
        if text.startswith("```"):
            text = text.split("```", 2)[1]
            if text.startswith("json"):
                text = text[4:]
        start, end = text.find("{"), text.rfind("}")
        if start == -1 or end == -1:
            return {}
        try:
            return json.loads(text[start : end + 1])
        except json.JSONDecodeError:
            return {}

    @staticmethod
    def _to_sentiment(value) -> Sentiment:
        try:
            return Sentiment(value)
        except (ValueError, TypeError):
            return Sentiment.UNKNOWN

    @staticmethod
    def _rating_fallback(review: Review) -> Review:
        if review.rating is None:
            review.sentiment = Sentiment.UNKNOWN
        elif review.rating >= 4:
            review.sentiment = Sentiment.POSITIVE
        elif review.rating <= 2:
            review.sentiment = Sentiment.NEGATIVE
        else:
            review.sentiment = Sentiment.NEUTRAL
        review.sentiment_reason = "별점 기반(폴백)"
        return review
