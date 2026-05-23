from __future__ import annotations

from typing import Any

from fastapi import APIRouter, Depends
from pydantic import BaseModel, Field

from app.api.deps import get_content_service
from app.services.content_service import ContentGenerationRequest, ContentService, ContentType

router = APIRouter(prefix="/content", tags=["content"])


class ContentGenerateRequest(BaseModel):
    content_type: ContentType
    context: dict[str, Any]
    tone: str = Field(default="professional", max_length=50)
    max_words: int = Field(default=200, ge=10, le=2000)


class ContentGenerateResponse(BaseModel):
    content: str
    content_type: ContentType
    model_used: str
    token_count: int


@router.post("/generate", response_model=ContentGenerateResponse)
async def generate_content(
    request: ContentGenerateRequest,
    content_service: ContentService = Depends(get_content_service),
) -> ContentGenerateResponse:
    result = await content_service.generate(
        ContentGenerationRequest(
            content_type=request.content_type,
            context=request.context,
            tone=request.tone,
            max_words=request.max_words,
        )
    )
    return ContentGenerateResponse(
        content=result.content,
        content_type=result.content_type,
        model_used=result.model_used,
        token_count=result.token_count,
    )
