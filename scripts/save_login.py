"""Manually log in to a platform once and save the session for reuse.

Usage:
    python scripts/save_login.py baemin https://ceo.baemin.com/...
    python scripts/save_login.py coupang_eats https://store.coupangeats.com/...

A visible browser opens. Log in by hand (including any captcha/2FA), then
return to the terminal and press Enter. The auth cookies are saved to
data/sessions/<platform>.json and reused by the crawler.
"""

from __future__ import annotations

import asyncio
import sys

from playwright.async_api import async_playwright

# Allow running as a script from the repo root.
sys.path.insert(0, ".")

from app.models import Platform  # noqa: E402
from app.session import session_file  # noqa: E402


async def main(platform: Platform, url: str) -> None:
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=False)
        context = await browser.new_context(locale="ko-KR")
        page = await context.new_page()
        await page.goto(url)

        print(f"\n[{platform.label}] 브라우저에서 로그인을 완료한 뒤,")
        input("로그인이 끝나면 이 터미널에서 Enter 를 누르세요... ")

        path = session_file(platform)
        await context.storage_state(path=str(path))
        print(f"세션 저장 완료: {path}")
        await browser.close()


if __name__ == "__main__":
    if len(sys.argv) < 3:
        print("usage: python scripts/save_login.py <platform> <login_url>")
        print("  platform: " + ", ".join(p.value for p in Platform))
        raise SystemExit(1)
    try:
        plat = Platform(sys.argv[1])
    except ValueError:
        print(f"unknown platform: {sys.argv[1]}")
        raise SystemExit(1)
    asyncio.run(main(plat, sys.argv[2]))
