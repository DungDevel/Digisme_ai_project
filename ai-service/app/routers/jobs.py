"""Internal endpoints cho pos-api gọi — chạy trong Sprint 2-3.

Phase 0: chỉ có ping để xác nhận đường đi nội bộ + token auth hoạt động.
"""
from fastapi import APIRouter, Depends

from app.routers.health import verify_internal

router = APIRouter()


@router.post("/notify/stock-low")
def notify_stock_low(payload: dict, _=Depends(verify_internal)):
    """pos-api đẩy sự kiện hết hàng ở đây → ai-service forward SignalR (Sprint 2)."""
    return {"received": True, "event": "stock-low", "items": payload.get("items", [])}


@router.post("/ping")
def ping(_=Depends(verify_internal)):
    return {"pong": True}
