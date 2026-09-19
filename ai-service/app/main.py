"""AI Service — FastAPI.

Chạy độc lập, đọc DB qua user read-only (schema public SELECT only).
Sprint 1-2: /health + mock endpoints chuẩn bị contract.
Sprint 3:   OCR pipeline + Copilot engine + background jobs.
"""
from fastapi import FastAPI

from app.routers import health, invoices, copilot, jobs

app = FastAPI(
    title="DigiSME AI Service",
    version="0.1.0",
    description=(
        "AI backend cho POS: OCR hóa đơn nhà cung cấp, AI Business Copilot, "
        "background jobs (cảnh báo kho, poll VietQR, sales velocity). "
        "Phase 0: health + endpoint mẫu."
    ),
)

app.include_router(health.router)
app.include_router(invoices.router, prefix="/api/v1/ai/invoices", tags=["OCR Invoice"])
app.include_router(copilot.router, prefix="/api/v1/ai/copilot", tags=["AI Copilot"])
app.include_router(jobs.router, prefix="/api/v1/ai/jobs", tags=["Internal Jobs"])

if __name__ == "__main__":
    import uvicorn
    uvicorn.run("app.main:app", host="0.0.0.0", port=8001, reload=True)
