from __future__ import annotations

from pathlib import Path

from app.config import settings
from app.models import Platform


def session_file(platform: Platform) -> Path:
    return settings.session_path / f"{platform.value}.json"


def has_session(platform: Platform) -> bool:
    return session_file(platform).exists()


def storage_state_arg(platform: Platform) -> str | None:
    """Path to a saved Playwright storage_state for this platform, if any."""
    path = session_file(platform)
    return str(path) if path.exists() else None
