from __future__ import annotations

import asyncio
from collections.abc import AsyncGenerator
from contextlib import asynccontextmanager

import structlog
from fastapi import FastAPI

from app.api import health
from app.api.v1 import router as v1_router
from app.infrastructure.database import create_engine_and_session_factory
from app.services.chat_service import ChatService
from app.services.content_service import ContentService
from app.services.embedding_service import EmbeddingService
from app.services.rag_service import RagService
from app.settings import Settings
from app.workers.catalog_sync_worker import CatalogSyncWorker
from app.workers.rabbitmq_worker import run_rabbitmq_workers

logger = structlog.get_logger()


def _task_done_callback(task: asyncio.Task[None]) -> None:
    """Log unhandled exceptions from background tasks instead of silently losing them."""
    if task.cancelled():
        return
    exc = task.exception()
    if exc:
        logger.error("background_task_failed", task_name=task.get_name(), error=str(exc))


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncGenerator[None, None]:
    settings = Settings()
    settings.validate_required()

    engine, session_factory = create_engine_and_session_factory(settings)
    app.state.session_factory = session_factory
    app.state.settings = settings

    embedding_service = EmbeddingService(settings)
    rag_service = RagService(embedding_service)
    chat_service = ChatService(settings, rag_service)
    content_service = ContentService(settings)

    app.state.rag_service = rag_service
    app.state.chat_service = chat_service
    app.state.content_service = content_service
    app.state.embedding_service = embedding_service

    background_tasks: list[asyncio.Task[None]] = []

    rabbitmq_task = asyncio.create_task(
        run_rabbitmq_workers(settings, session_factory),
        name="rabbitmq-workers",
    )
    rabbitmq_task.add_done_callback(_task_done_callback)
    background_tasks.append(rabbitmq_task)

    if settings.catalog_sync_on_startup:
        sync_worker = CatalogSyncWorker(settings, session_factory)

        async def _sync_lifecycle() -> None:
            await sync_worker.run_once()
            await sync_worker.run_periodic(settings.catalog_sync_interval_seconds)

        sync_task = asyncio.create_task(_sync_lifecycle(), name="catalog-sync")
        sync_task.add_done_callback(_task_done_callback)
        background_tasks.append(sync_task)

    logger.info("ai_service_started", model_fast=settings.llm_model_fast)

    yield

    for task in background_tasks:
        task.cancel()
        try:
            await task
        except asyncio.CancelledError:
            pass

    await embedding_service.close()
    await engine.dispose()
    logger.info("ai_service_stopped")


app = FastAPI(
    title="Haworks AI Service",
    version="0.1.0",
    lifespan=lifespan,
)

app.include_router(health.router)
app.include_router(v1_router, prefix="/api/v1/ai")
