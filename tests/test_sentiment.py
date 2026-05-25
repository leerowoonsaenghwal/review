from app.models import Platform, Review, Sentiment
from app.sentiment import SentimentAnalyzer


def make(rating=None, text="맛있어요"):
    return Review(platform=Platform.NAVER, text=text, rating=rating)


def test_rating_fallback_when_no_api_key():
    analyzer = SentimentAnalyzer(api_key="")
    assert not analyzer.available
    reviews = [make(rating=5), make(rating=1), make(rating=3), make(rating=None)]
    out = analyzer.analyze(reviews)
    assert out[0].sentiment == Sentiment.POSITIVE
    assert out[1].sentiment == Sentiment.NEGATIVE
    assert out[2].sentiment == Sentiment.NEUTRAL
    assert out[3].sentiment == Sentiment.UNKNOWN


def test_extract_json_plain():
    data = SentimentAnalyzer._extract_json('{"results": [{"index": 0}]}')
    assert data["results"][0]["index"] == 0


def test_extract_json_fenced():
    raw = '```json\n{"results": [{"index": 1, "sentiment": "negative"}]}\n```'
    data = SentimentAnalyzer._extract_json(raw)
    assert data["results"][0]["sentiment"] == "negative"


def test_extract_json_with_prose():
    raw = '분석 결과입니다: {"results": []} 이상입니다.'
    assert SentimentAnalyzer._extract_json(raw) == {"results": []}


def test_extract_json_invalid():
    assert SentimentAnalyzer._extract_json("not json at all") == {}


def test_to_sentiment():
    assert SentimentAnalyzer._to_sentiment("positive") == Sentiment.POSITIVE
    assert SentimentAnalyzer._to_sentiment("garbage") == Sentiment.UNKNOWN


def test_empty_reviews_noop():
    assert SentimentAnalyzer(api_key="").analyze([]) == []
