from app.crawlers.registry import crawler_for_url, platform_for_url
from app.models import Platform


def test_naver_dispatch():
    url = "https://m.place.naver.com/restaurant/12345/review/visitor"
    assert platform_for_url(url) == Platform.NAVER


def test_kakao_dispatch():
    assert platform_for_url("https://place.map.kakao.com/98765") == Platform.KAKAO


def test_baemin_dispatch():
    assert platform_for_url("https://ceo.baemin.com/shop/1/reviews") == Platform.BAEMIN


def test_coupang_eats_dispatch():
    url = "https://store.coupangeats.com/merchant/reviews/1"
    assert platform_for_url(url) == Platform.COUPANG_EATS


def test_unknown_url():
    assert crawler_for_url("https://example.com/foo") is None


def test_requires_login_flags():
    assert Platform.BAEMIN.requires_login
    assert Platform.COUPANG_EATS.requires_login
    assert not Platform.NAVER.requires_login
    assert not Platform.KAKAO.requires_login
