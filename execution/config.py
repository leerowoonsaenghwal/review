"""Shared configuration for the Instagram analysis pipeline.

Loads secrets and paths from the project-root ``.env`` and exposes them to the
execution-layer scripts. Keep this the single source of truth for keys, the
Apify actor id, the Gemini model, and the intermediate/output file locations.
"""

from __future__ import annotations

import os
from pathlib import Path

from dotenv import load_dotenv

# Project root = parent of the execution/ directory.
BASE_DIR = Path(__file__).resolve().parent.parent

load_dotenv(BASE_DIR / ".env")

# --- Secrets ---------------------------------------------------------------
APIFY_API_TOKEN = os.getenv("APIFY_API_TOKEN", "").strip()
GEMINI_API_KEY = os.getenv("GEMINI_API_KEY", "").strip()

# --- Tunables --------------------------------------------------------------
# Apify "Instagram Post Scraper". Override via env if you prefer another actor
# (e.g. apify/instagram-scraper) — the input builder in instagram_scraper.py
# targets the Post Scraper schema.
APIFY_ACTOR_ID = os.getenv("APIFY_ACTOR_ID", "apify/instagram-post-scraper").strip()
GEMINI_MODEL = os.getenv("GEMINI_MODEL", "gemini-2.5-flash").strip()
# How many posts to pull / analyze per run.
RESULTS_LIMIT = int(os.getenv("RESULTS_LIMIT", "30"))

# --- Paths -----------------------------------------------------------------
TMP_DIR = BASE_DIR / ".tmp"
OUTPUT_DIR = BASE_DIR / "Output"

RAW_DATA_PATH = TMP_DIR / "raw_data.json"
ANALYSIS_PATH = TMP_DIR / "analysis.json"
REPORT_PATH = OUTPUT_DIR / "analysis_report.md"


def ensure_dirs() -> None:
    TMP_DIR.mkdir(parents=True, exist_ok=True)
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
