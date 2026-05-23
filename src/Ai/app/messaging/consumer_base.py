from __future__ import annotations

import json
from abc import ABC, abstractmethod
from typing import TYPE_CHECKING, Any

import structlog

from app.infrastructure.database import check_and_insert_inbox
from app.messaging.masstransit import MassTransitEnvelope

if TYPE_CHECKING:
    from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

logger = structlog.get_logger()


class IdempotentConsumerBase(ABC):
    @property
    @abstractmethod
    def expected_message_type_suffix(self) -> str:
        """e.g. ':OrderCompletedEvent' — the urn suffix to match."""

    @abstractmethod
    async def handle(self, message: dict[str, Any], session: AsyncSession) -> None:
        """Business logic to run once per unique message."""

    async def process(self, raw_body: bytes, session_factory: async_sessionmaker[AsyncSession]) -> None:
        envelope_data = json.loads(raw_body)

        message_types: list[str] = envelope_data.get("messageType", [])
        if not any(mt.endswith(self.expected_message_type_suffix) for mt in message_types):
            logger.debug(
                "message_type_mismatch",
                expected=self.expected_message_type_suffix,
                actual=message_types,
            )
            return

        envelope = MassTransitEnvelope.model_validate(envelope_data)

        async with session_factory() as session:
            async with session.begin():
                is_duplicate = await check_and_insert_inbox(session, envelope.message_id)
                if is_duplicate:
                    return

                message_dict = envelope_data.get("message", {})
                await self.handle(message_dict, session)

        logger.info(
            "message_processed",
            message_id=str(envelope.message_id),
            message_type=self.expected_message_type_suffix,
        )
