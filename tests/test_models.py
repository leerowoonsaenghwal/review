from app.models import CrawlJob, Platform, Review, Sentiment


def test_fingerprint_stable_and_dedupes():
    a = Review(platform=Platform.NAVER, author="김철수", text="맛있어요", date="2024.01.01")
    b = Review(platform=Platform.NAVER, author="김철수", text="맛있어요", date="2024.01.01")
    c = Review(platform=Platform.NAVER, author="이영희", text="별로예요", date="2024.01.02")
    assert a.fingerprint == b.fingerprint
    assert a.fingerprint != c.fingerprint


def test_base_rating_parse():
    from app.crawlers.base import BaseCrawler

    assert BaseCrawler.parse_rating("5.0") == 5.0
    assert BaseCrawler.parse_rating("별점 4") == 4.0
    assert BaseCrawler.parse_rating("★★★★☆") == 4.0
    assert BaseCrawler.parse_rating("9999") is None
    assert BaseCrawler.parse_rating("") is None


def test_job_sentiment_grouping():
    job = CrawlJob(id="x", url="u", platform=Platform.NAVER)
    job.reviews = [
        Review(platform=Platform.NAVER, text="a", sentiment=Sentiment.POSITIVE),
        Review(platform=Platform.NAVER, text="b", sentiment=Sentiment.NEGATIVE),
        Review(platform=Platform.NAVER, text="c", sentiment=Sentiment.NEUTRAL),
        Review(platform=Platform.NAVER, text="d", sentiment=Sentiment.UNKNOWN),
    ]
    assert len(job.positive) == 1
    assert len(job.negative) == 1
    assert len(job.other) == 2


def test_platform_labels():
    assert Platform.NAVER.label == "네이버 플레이스"
    assert Platform.COUPANG_EATS.label == "쿠팡이츠"
