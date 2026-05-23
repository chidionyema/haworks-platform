from __future__ import annotations

import asyncio
import json
from collections.abc import AsyncGenerator
from datetime import UTC, datetime
from typing import TYPE_CHECKING

import structlog
from langchain_anthropic import ChatAnthropic
from langchain_core.messages import AIMessage, HumanMessage, SystemMessage
from sqlalchemy import select

from app.domain.models import ChatSession
from app.services.rag_service import RagService

if TYPE_CHECKING:
    from sqlalchemy.ext.asyncio import AsyncSession

    from app.settings import Settings

logger = structlog.get_logger()

SYSTEM_PROMPT = """You are a helpful shopping assistant for the Haworks marketplace.
You help users find products, answer questions about items, and provide recommendations.
When users ask about products, use the provided search context to give accurate answers.
Be concise, friendly, and helpful. If you don't have information about something, say so honestly."""

MAX_HISTORY_MESSAGES = 20
MAX_PERSISTED_MESSAGES = 100


class ChatService:
    def __init__(
        self,
        settings: Settings,
        rag_service: RagService,
    ) -> None:
        self._rag_service = rag_service
        self._llm = ChatAnthropic(
            model=settings.llm_model_fast,
            api_key=settings.anthropic_api_key,
            streaming=True,
            max_tokens=1024,
        )

    async def _load_or_create_session(
        self, session: AsyncSession, session_id: str, user_id: str | None
    ) -> ChatSession:
        result = await session.execute(
            select(ChatSession).where(ChatSession.session_id == session_id)
        )
        chat_session = result.scalar_one_or_none()
        if chat_session is None:
            chat_session = ChatSession(
                session_id=session_id,
                user_id=user_id,
                messages=[],
            )
            session.add(chat_session)
            await session.flush()
        return chat_session

    def _build_messages(
        self, chat_session: ChatSession, user_message: str, context: str
    ) -> list[SystemMessage | HumanMessage | AIMessage]:
        messages: list[SystemMessage | HumanMessage | AIMessage] = [SystemMessage(content=SYSTEM_PROMPT)]

        history: list[dict[str, str]] = chat_session.messages or []
        for msg in history[-MAX_HISTORY_MESSAGES:]:
            if msg["role"] == "user":
                messages.append(HumanMessage(content=msg["content"]))
            elif msg["role"] == "assistant":
                messages.append(AIMessage(content=msg["content"]))

        prompt = user_message
        if context:
            prompt = f"[Search Context]\n{context}\n\n[User Question]\n{user_message}"

        messages.append(HumanMessage(content=prompt))
        return messages

    async def stream_response(
        self,
        session: AsyncSession,
        session_id: str,
        message: str,
        user_id: str | None = None,
    ) -> AsyncGenerator[str, None]:
        chat_session = await self._load_or_create_session(session, session_id, user_id)

        context = ""
        try:
            search_results = await self._rag_service.search(session, message, top_k=5, min_score=0.3)
            if search_results:
                context_parts = [
                    f"- {r.name} (${r.unit_price:.2f}): {r.description[:200]}"
                    for r in search_results
                ]
                context = "\n".join(context_parts)
        except Exception:
            logger.warning("rag_search_failed_for_chat", session_id=session_id)

        messages = self._build_messages(chat_session, message, context)

        full_response = ""
        try:
            async for chunk in self._llm.astream(messages):
                token = chunk.content
                if isinstance(token, str) and token:
                    full_response += token
                    yield f"data: {json.dumps({'token': token})}\n\n"
        except asyncio.CancelledError:
            logger.info("chat_stream_client_disconnected", session_id=session_id)
        except Exception:
            logger.exception("chat_stream_llm_error", session_id=session_id)
            yield f"data: {json.dumps({'error': 'An error occurred generating the response.'})}\n\n"

        if full_response:
            history = list(chat_session.messages or [])
            history.append({"role": "user", "content": message, "ts": datetime.now(UTC).isoformat()})
            history.append({"role": "assistant", "content": full_response, "ts": datetime.now(UTC).isoformat()})
            # Trim persisted history to prevent unbounded JSONB growth
            chat_session.messages = history[-MAX_PERSISTED_MESSAGES:]
            chat_session.updated_at = datetime.now(UTC)
            await session.commit()

        yield "data: [DONE]\n\n"
        logger.info("chat_stream_completed", session_id=session_id, response_length=len(full_response))
