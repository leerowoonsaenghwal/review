from __future__ import annotations

import logging
import uuid

from fastapi import BackgroundTasks, FastAPI, Form, Request
from fastapi.responses import HTMLResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates

from app.config import BASE_DIR, settings
from app.crawl_service import (
    LoginRequiredError,
    UnsupportedUrlError,
    crawl_reviews,
)
from app.crawlers.registry import platform_for_url
from app.models import CrawlJob, JobStatus

logging.basicConfig(level=logging.INFO)

app = FastAPI(title="리뷰 크롤링 에이전트")
templates = Jinja2Templates(directory=str(BASE_DIR / "app" / "templates"))
app.mount("/static", StaticFiles(directory=str(BASE_DIR / "app" / "static")), name="static")
app.mount("/shots", StaticFiles(directory=str(settings.screenshot_path)), name="shots")

# In-memory job store. Swap for Redis/DB to scale beyond one process.
JOBS: dict[str, CrawlJob] = {}


@app.get("/", response_class=HTMLResponse)
async def index(request: Request):
    return templates.TemplateResponse("index.html", {"request": request})


@app.post("/crawl")
async def start_crawl(background: BackgroundTasks, url: str = Form(...)):
    url = url.strip()
    platform = platform_for_url(url)
    if platform is None:
        return JSONResponse(
            {"error": "지원하지 않는 URL입니다. 네이버/카카오/배민/쿠팡이츠 링크를 입력하세요."},
            status_code=400,
        )
    job_id = uuid.uuid4().hex[:12]
    JOBS[job_id] = CrawlJob(id=job_id, url=url, platform=platform)
    background.add_task(_run_job, job_id)
    return {"job_id": job_id, "platform": platform.value}


async def _run_job(job_id: str) -> None:
    job = JOBS[job_id]
    job.status = JobStatus.RUNNING
    try:
        job.reviews = await crawl_reviews(job.url)
        job.status = JobStatus.DONE
        job.message = f"{len(job.reviews)}개 리뷰 수집 완료"
    except (LoginRequiredError, UnsupportedUrlError) as exc:
        job.status = JobStatus.ERROR
        job.message = str(exc)
    except Exception as exc:  # pragma: no cover - surfaced to the UI
        logging.exception("crawl job failed")
        job.status = JobStatus.ERROR
        job.message = f"크롤링 중 오류: {exc}"


@app.get("/jobs/{job_id}/status")
async def job_status(job_id: str):
    job = JOBS.get(job_id)
    if not job:
        return JSONResponse({"error": "unknown job"}, status_code=404)
    return {
        "status": job.status.value,
        "message": job.message,
        "counts": {
            "total": len(job.reviews),
            "positive": len(job.positive),
            "negative": len(job.negative),
            "other": len(job.other),
        },
    }


@app.get("/jobs/{job_id}", response_class=HTMLResponse)
async def job_result(request: Request, job_id: str):
    job = JOBS.get(job_id)
    if not job:
        return HTMLResponse("<h1>존재하지 않는 작업입니다.</h1>", status_code=404)
    return templates.TemplateResponse(
        "results.html", {"request": request, "job": job}
    )
