from __future__ import annotations

from fastapi import APIRouter, Depends
from fastapi.responses import StreamingResponse
from pydantic import BaseModel, Field
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.deps import get_chat_service, get_db_session
from app.services.chat_service import ChatService

router = APIRouter(prefix="/chat", tags=["chat"])


class ChatMessageRequest(BaseModel):
    session_id: str = Field(min_length=1, max_length=200)
    message: str = Field(min_length=1, max_length=5000)
    user_id: str | None = Field(default=None, max_length=200)


@router.post("/message")
async def chat_message(
    request: ChatMessageRequest,
    session: AsyncSession = Depends(get_db_session),
    chat_service: ChatService = Depends(get_chat_service),
) -> StreamingResponse:
    generator = chat_service.stream_response(
        session=session,
        session_id=request.session_id,
        message=request.message,
        user_id=request.user_id,
    )
    return StreamingResponse(
        generator,
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "X-Accel-Buffering": "no",
        },
    )
