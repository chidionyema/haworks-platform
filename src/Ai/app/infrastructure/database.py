from __future__ import annotations

import uuid
from datetime import UTC, datetime
from typing import TYPE_CHECKING

import structlog
from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

if TYPE_CHECKING:
    from app.settings import Settings

logger = structlog.get_logger()


def create_engine_and_session_factory(
    settings: Settings,
) -> tuple[object, async_sessionmaker[AsyncSession]]:
    engine = create_async_engine(
        settings.database_url,
        pool_size=10,
        max_overflow=5,
        pool_pre_ping=True,
        echo=False,
    )
    session_factory = async_sessionmaker(engine, expire_on_commit=False)
    return engine, session_factory


async def check_and_insert_inbox(session: AsyncSession, message_id: uuid.UUID) -> bool:
    """Return True if the message was already processed (duplicate).

    Uses INSERT ... ON CONFLICT DO NOTHING to avoid TOCTOU race conditions.
    Two concurrent deliveries of the same message_id will both attempt the
    INSERT; the loser gets rowcount=0 without raising IntegrityError, which
    keeps the session healthy for subsequent operations.
    """
    result = await session.execute(
        text(
            "INSERT INTO ai.inbox_state (message_id, processed_at) "
            "VALUES (:msg_id, :now) "
            "ON CONFLICT (message_id) DO NOTHING"
        ),
        {"msg_id": message_id, "now": datetime.now(UTC)},
    )
    if result.rowcount == 0:
        logger.info("duplicate_message_skipped", message_id=str(message_id))
        return True
    return False
