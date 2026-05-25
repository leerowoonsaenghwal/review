from pathlib import Path

from pydantic_settings import BaseSettings, SettingsConfigDict

BASE_DIR = Path(__file__).resolve().parent.parent


class Settings(BaseSettings):
    model_config = SettingsConfigDict(
        env_file=".env", env_file_encoding="utf-8", extra="ignore"
    )

    anthropic_api_key: str = ""
    sentiment_model: str = "claude-haiku-4-5-20251001"

    headless: bool = True
    max_reviews: int = 100

    session_dir: str = "data/sessions"
    screenshot_dir: str = "data/screenshots"

    @property
    def session_path(self) -> Path:
        p = BASE_DIR / self.session_dir
        p.mkdir(parents=True, exist_ok=True)
        return p

    @property
    def screenshot_path(self) -> Path:
        p = BASE_DIR / self.screenshot_dir
        p.mkdir(parents=True, exist_ok=True)
        return p


settings = Settings()
