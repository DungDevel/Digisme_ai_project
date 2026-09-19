"""OCR hóa đơn nhà cung cấp.

Phase 0: endpoint mẫu trả mock — contract đã chốt để Frontend/pos-api build tiếp.
Sprint 3: thay _extract_mock bằng pipeline thật (LLM Vision structured output,
fallback Tesseract), giữ nguyên shape response.
"""
import uuid
from fastapi import APIRouter, UploadFile, File, HTTPException

from app.config import settings

router = APIRouter()


@router.post("/extract")
async def extract_invoice(file: UploadFile = File(...)):
    """Nhận ảnh hóa đơn giấy từ nhà cung cấp, trả JSON dữ liệu đã trích xuất.

    Response shape (chốt phase 0, không đổi ở Sprint 3):
    {
      "invoiceId": "uuid",
      "overallConfidence": 0.0-1.0,
      "items": [{"name","unit","qty","unitPrice","confidence"}],
      "total": số | null,
      "supplierName": str | null,
      "engine": "llm-vision" | "tesseract" | "mock",
      "reviewRequired": true/false
    }
    Frontend hiển thị side-by-side: ảnh gốc (bên trái) + bảng items (bên phải).
    """
    data = await file.read()
    if len(data) < 10:
        raise HTTPException(400, "File ảnh rỗng")
    if len(data) > 15 * 1024 * 1024:
        raise HTTPException(413, "Ảnh tối đa 15MB")

    if not settings.llm_api_key:
        # Phase 0: chưa có LLM key → trả mock rõ ràng
        return {
            "invoiceId": str(uuid.uuid4()),
            "overallConfidence": 0.0,
            "items": [
                {"name": "(Mẫu) Nước suối Lavie 500ml", "unit": "chai", "qty": 24, "unitPrice": 3500, "confidence": 0.0},
                {"name": "(Mẫu) Bánh quy Oreo", "unit": "gói", "qty": 48, "unitPrice": 12000, "confidence": 0.0},
            ],
            "total": None,
            "supplierName": None,
            "engine": "mock",
            "reviewRequired": True,
            "note": "LLM chưa cấu hình — đây là dữ liệu MẪU. Cấu hình LLM_API_KEY để chạy OCR thật (Sprint 3).",
        }

    # Sprint 3: pipeline thật
    raise HTTPException(501, "OCR pipeline sẽ được implement ở Sprint 3")
