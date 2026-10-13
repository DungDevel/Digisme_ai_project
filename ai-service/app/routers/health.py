from fastapi import APIRouter, Depends
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer

import psycopg
from fastapi import HTTPException
from app.config import settings

router = APIRouter()
security = HTTPBearer()


def verify_internal(token: HTTPAuthorizationCredentials = Depends(security)):
    """Endpoint nội bộ: FastAPI gọi từ pos-api, xác nhận bằng token nội bộ."""
    if token.credentials != settings.internal_token:
        # P0: token sai → 401 Unauthorized (chuẩn REST), không RuntimeError→500
        raise HTTPException(status_code=401, detail="Internal token mismatch")
    return token


@router.get("/health")
def health():
    db_ok = True
    try:
        with psycopg.connect(settings.postgres_dsn) as conn:
            conn.execute("SELECT 1")
    except Exception:
        db_ok = False
    return {
        "status": "ok" if db_ok else "degraded",
        "service": "ai-service",
        "version": "0.1.0",
        "database": db_ok,
    }
