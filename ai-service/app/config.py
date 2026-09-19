"""Config — đọc từ env (docker compose cấp)."""
from pydantic_settings import BaseSettings


class Settings(BaseSettings):
    postgres_dsn: str = "postgresql://ai_reader:ai_reader_pass@localhost:5432/digism_pos"
    pos_api_base: str = "http://localhost:8000"
    internal_token: str = "ai-internal-dev-token"
    llm_api_key: str = ""          # OpenAI key (Sprint 3 — OCR + Copilot)
    llm_model: str = "gpt-4o"
    ocr_engine: str = "llm-vision"  # llm-vision | tesseract

    class Config:
        env_file = ".env"


settings = Settings()
