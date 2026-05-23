from __future__ import annotations

from typing import Any

import structlog
from sqlalchemy.ext.asyncio import AsyncSession

from app.messaging.consumer_base import IdempotentConsumerBase
from app.messaging.masstransit import OrderCompletedMessage
from app.services.recommendation_service import RecommendationService

logger = structlog.get_logger()


class OrderCompletedConsumer(IdempotentConsumerBase):
    def __init__(self, recommendation_service: RecommendationService) -> None:
        self._recommendation_service = recommendation_service

    @property
    def expected_message_type_suffix(self) -> str:
        return ":OrderCompletedEvent"

    async def handle(self, message: dict[str, Any], session: AsyncSession) -> None:
        msg = OrderCompletedMessage.model_validate(message)

        await self._recommendation_service.record_order_signal(
            session=session,
            customer_id=msg.customer_id,
            order_id=msg.order_id,
            total_amount=msg.total_amount,
            completed_at=msg.completed_at,
        )

        logger.info(
            "order_completed_processed",
            order_id=str(msg.order_id),
            customer_id=msg.customer_id,
        )
