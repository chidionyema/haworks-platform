from __future__ import annotations

import asyncio
from typing import TYPE_CHECKING

import aio_pika
import structlog

from app.infrastructure.catalog_client import CatalogClient
from app.messaging.consumers.order_completed import OrderCompletedConsumer
from app.messaging.consumers.product_invalidated import ProductInvalidatedConsumer
from app.services.catalog_sync_service import CatalogSyncService
from app.services.embedding_service import EmbeddingService
from app.services.recommendation_service import RecommendationService

if TYPE_CHECKING:
    from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

    from app.settings import Settings

logger = structlog.get_logger()

QUEUE_ORDER_COMPLETED = "ai-svc:order-completed"
QUEUE_PRODUCT_INVALIDATED = "ai-svc:product-invalidated"

EXCHANGE_ORDER_COMPLETED = "HaWorks.BuildingBlocks.SharedContracts:OrderCompletedEvent"
EXCHANGE_PRODUCT_INVALIDATED = "HaWorks.BuildingBlocks.SharedContracts:ProductCacheInvalidatedEvent"


async def run_rabbitmq_workers(
    settings: Settings,
    session_factory: async_sessionmaker[AsyncSession],
) -> None:
    """Connect to RabbitMQ and consume events. Blocks forever via asyncio.Future.

    Uses connect_robust for automatic reconnection. On channel/consumer errors,
    the entire setup is retried with exponential backoff.
    """
    backoff = 1.0
    max_backoff = 60.0

    while True:
        try:
            connection = await aio_pika.connect_robust(
                settings.rabbitmq_url,
                heartbeat=10,
            )

            channel = await connection.channel()
            await channel.set_qos(prefetch_count=10)

            recommendation_service = RecommendationService()
            embedding_service = EmbeddingService(settings)
            catalog_client = CatalogClient(settings.catalog_base_url)
            catalog_sync_service = CatalogSyncService(catalog_client, embedding_service, session_factory)

            order_consumer = OrderCompletedConsumer(recommendation_service)
            product_consumer = ProductInvalidatedConsumer(catalog_sync_service)

            order_exchange = await channel.declare_exchange(
                EXCHANGE_ORDER_COMPLETED,
                aio_pika.ExchangeType.FANOUT,
                passive=True,
            )
            product_exchange = await channel.declare_exchange(
                EXCHANGE_PRODUCT_INVALIDATED,
                aio_pika.ExchangeType.FANOUT,
                passive=True,
            )

            order_queue = await channel.declare_queue(QUEUE_ORDER_COMPLETED, durable=True)
            await order_queue.bind(order_exchange)

            product_queue = await channel.declare_queue(QUEUE_PRODUCT_INVALIDATED, durable=True)
            await product_queue.bind(product_exchange)

            async def on_order_completed(message: aio_pika.abc.AbstractIncomingMessage) -> None:
                async with message.process():
                    try:
                        await order_consumer.process(message.body, session_factory)
                    except Exception:
                        logger.exception("order_completed_consumer_error")

            async def on_product_invalidated(message: aio_pika.abc.AbstractIncomingMessage) -> None:
                async with message.process():
                    try:
                        await product_consumer.process(message.body, session_factory)
                    except Exception:
                        logger.exception("product_invalidated_consumer_error")

            await order_queue.consume(on_order_completed)
            await product_queue.consume(on_product_invalidated)

            logger.info(
                "rabbitmq_consumers_started",
                queues=[QUEUE_ORDER_COMPLETED, QUEUE_PRODUCT_INVALIDATED],
            )
            backoff = 1.0

            # Block forever — consumers run via aio-pika callbacks.
            # If the connection drops, connect_robust reconnects, but the
            # channel/consumers may be lost. We catch that below.
            await asyncio.Future()

        except asyncio.CancelledError:
            logger.info("rabbitmq_worker_cancelled")
            raise
        except Exception:
            logger.exception("rabbitmq_worker_error", retry_in=backoff)
            await asyncio.sleep(backoff)
            backoff = min(backoff * 2, max_backoff)
