from __future__ import annotations

from collections.abc import AsyncGenerator

from fastapi import Depends, Request
from sqlalchemy.ext.asyncio import AsyncSession

from app.services.chat_service import ChatService
from app.services.content_service import ContentService
from app.services.rag_service import RagService
from app.services.recommendation_service import RecommendationService


async def get_db_session(request: Request) -> AsyncGenerator[AsyncSession, None]:
    session_factory = request.app.state.session_factory
    async with session_factory() as session:
        yield session


async def get_rag_service(request: Request) -> RagService:
    return request.app.state.rag_service


async def get_chat_service(request: Request) -> ChatService:
    return request.app.state.chat_service


async def get_content_service(request: Request) -> ContentService:
    return request.app.state.content_service


async def get_recommendation_service() -> RecommendationService:
    return RecommendationService()
