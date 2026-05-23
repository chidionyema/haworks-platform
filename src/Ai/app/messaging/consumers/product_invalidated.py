from __future__ import annotations

from typing import Any

import structlog
from sqlalchemy.ext.asyncio import AsyncSession

from app.messaging.consumer_base import IdempotentConsumerBase
from app.messaging.masstransit import ProductCacheInvalidatedMessage
from app.services.catalog_sync_service import CatalogSyncService

logger = structlog.get_logger()


class ProductInvalidatedConsumer(IdempotentConsumerBase):
    def __init__(self, catalog_sync_service: CatalogSyncService) -> None:
        self._catalog_sync_service = catalog_sync_service

    @property
    def expected_message_type_suffix(self) -> str:
        return ":ProductCacheInvalidatedEvent"

    async def handle(self, message: dict[str, Any], session: AsyncSession) -> None:
        msg = ProductCacheInvalidatedMessage.model_validate(message)

        synced = await self._catalog_sync_service.sync_single(str(msg.product_id))

        logger.info(
            "product_invalidated_processed",
            product_id=str(msg.product_id),
            synced=synced,
        )
