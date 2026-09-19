"""AI Business Copilot — Text-to-Insights.

Phase 0: endpoint mẫu trả mock, contract đã chốt.
Sprint 3: Function Calling (LLM chọn function + tham số → query SQL đã viết sẵn
→ LLM tổng hợp câu trả lời tiếng Việt). KHÔNG dùng Text-to-SQL tự do.
"""
from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from app.config import settings

router = APIRouter()


class CopilotQuestion(BaseModel):
    question: str
    conversationId: str | None = None


@router.post("/ask")
async def ask(question: CopilotQuestion):
    """Trả về:
    {
      "answer": câu trả lời tiếng Việt,
      "data": [...],             # số liệu thô để Frontend vẽ chart
      "chartType": "bar"|"line"|"table"|null,
      "function": tên function đã gọi,
      "engine": "mock"|"llm"
    }
    """
    if not settings.llm_api_key:
        return {
            "answer": f"(Mẫu) Câu hỏi nhận được: \" {question.question} \". "
                      "Copilot cần cấu hình LLM_API_KEY để trả lời thật (Sprint 3).",
            "data": [],
            "chartType": None,
            "function": None,
            "engine": "mock",
        }
    raise HTTPException(501, "Copilot engine sẽ được implement ở Sprint 3")
