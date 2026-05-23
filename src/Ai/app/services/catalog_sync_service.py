from __future__ import annotations

import uuid
from datetime import UTC, datetime
from typing import TYPE_CHECKING

import structlog
from sqlalchemy import select

from app.domain.models import ProductEmbedding
from app.infrastructure.catalog_client import CatalogClient
from app.services.embedding_service import EmbeddingService

if TYPE_CHECKING:
    from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

logger = structlog.get_logger()


class CatalogSyncService:
    def __init__(
        self,
        catalog_client: CatalogClient,
        embedding_service: EmbeddingService,
        session_factory: async_sessionmaker[AsyncSession],
    ) -> None:
        self._catalog_client = catalog_client
        self._embedding_service = embedding_service
        self._session_factory = session_factory

    async def sync_all(self) -> int:
        logger.info("catalog_sync_started")
        total_synced = 0
        skip = 0
        batch_size = 50

        while True:
            products = await self._catalog_client.list_products(skip=skip, take=batch_size)
            if not products:
                break

            for product in products:
                try:
                    await self._sync_product(product)
                    total_synced += 1
                except Exception:
                    logger.exception("product_sync_failed", product_id=product.get("id"))

            skip += batch_size

            if len(products) < batch_size:
                break

        logger.info("catalog_sync_completed", total_synced=total_synced)
        return total_synced

    async def sync_single(self, product_id: str) -> bool:
        product = await self._catalog_client.get_product(product_id)
        if product is None:
            logger.warning("product_not_found_for_sync", product_id=product_id)
            return False
        await self._sync_product(product)
        return True

    async def _sync_product(self, product: dict) -> None:
        product_id = uuid.UUID(product["id"])
        name = product.get("name", "")
        description = product.get("description", "")
        unit_price = float(product.get("unitPrice", 0))
        category_id_raw = product.get("categoryId")
        category_id = uuid.UUID(category_id_raw) if category_id_raw else None
        category_name = product.get("categoryName", "")
        is_listed = product.get("isListed", True)
        is_in_stock = product.get("isInStock", True)
        source_version = int(product.get("version", 0))

        embedding = await self._embedding_service.embed_product(
            name=name,
            description=description,
            category_name=category_name,
            unit_price=unit_price,
        )

        async with self._session_factory() as session:
            async with session.begin():
                result = await session.execute(
                    select(ProductEmbedding).where(
                        ProductEmbedding.product_id == product_id
                    )
                )
                existing = result.scalar_one_or_none()

                if existing is not None:
                    if existing.source_version >= source_version:
                        logger.debug(
                            "skipping_stale_product_version",
                            product_id=str(product_id),
                            existing_version=existing.source_version,
                            incoming_version=source_version,
                        )
                        return

                    existing.name = name
                    existing.description = description
                    existing.unit_price = unit_price
                    existing.category_id = category_id
                    existing.category_name = category_name
                    existing.is_listed = is_listed
                    existing.is_in_stock = is_in_stock
                    existing.source_version = source_version
                    existing.embedding = embedding
                    existing.embedded_at = datetime.now(UTC)
                else:
                    session.add(
                        ProductEmbedding(
                            product_id=product_id,
                            name=name,
                            description=description,
                            unit_price=unit_price,
                            category_id=category_id,
                            category_name=category_name,
                            is_listed=is_listed,
                            is_in_stock=is_in_stock,
                            source_version=source_version,
                            embedding=embedding,
                            embedded_at=datetime.now(UTC),
                        )
                    )

        logger.debug("product_synced", product_id=str(product_id), version=source_version)
