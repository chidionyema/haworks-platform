from __future__ import annotations

import asyncio
from typing import TYPE_CHECKING

import structlog

from app.infrastructure.catalog_client import CatalogClient
from app.services.catalog_sync_service import CatalogSyncService
from app.services.embedding_service import EmbeddingService

if TYPE_CHECKING:
    from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

    from app.settings import Settings

logger = structlog.get_logger()


class CatalogSyncWorker:
    def __init__(
        self,
        settings: Settings,
        session_factory: async_sessionmaker[AsyncSession],
    ) -> None:
        self._settings = settings
        self._session_factory = session_factory
        self._catalog_client = CatalogClient(settings.catalog_base_url)
        self._embedding_service = EmbeddingService(settings)
        self._sync_service = CatalogSyncService(
            self._catalog_client, self._embedding_service, session_factory
        )

    async def run_once(self) -> int:
        try:
            count = await self._sync_service.sync_all()
            logger.info("catalog_sync_run_once_completed", synced=count)
            return count
        except Exception:
            logger.exception("catalog_sync_run_once_failed")
            return 0

    async def run_periodic(self, interval_seconds: int) -> None:
        logger.info("catalog_sync_periodic_started", interval=interval_seconds)
        while True:
            await self.run_once()
            await asyncio.sleep(interval_seconds)

    async def close(self) -> None:
        await self._catalog_client.close()
        await self._embedding_service.close()
