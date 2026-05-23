from __future__ import annotations

import uuid
from dataclasses import dataclass
from typing import TYPE_CHECKING

import structlog
from pgvector.sqlalchemy import Vector
from sqlalchemy import cast, select, text

from app.domain.models import ProductEmbedding
from app.services.embedding_service import EmbeddingService

if TYPE_CHECKING:
    from sqlalchemy.ext.asyncio import AsyncSession

logger = structlog.get_logger()


@dataclass(frozen=True)
class SearchResult:
    product_id: uuid.UUID
    name: str
    description: str
    unit_price: float
    score: float
    explanation: str


class RagService:
    def __init__(self, embedding_service: EmbeddingService) -> None:
        self._embedding_service = embedding_service

    async def search(
        self,
        session: AsyncSession,
        query: str,
        top_k: int = 10,
        min_score: float = 0.0,
    ) -> list[SearchResult]:
        query_embedding = await self._embedding_service.embed_text(query)

        distance_expr = ProductEmbedding.embedding.cosine_distance(cast(query_embedding, Vector(1024)))

        stmt = (
            select(
                ProductEmbedding.product_id,
                ProductEmbedding.name,
                ProductEmbedding.description,
                ProductEmbedding.unit_price,
                (1 - distance_expr).label("score"),
            )
            .where(
                ProductEmbedding.embedding.is_not(None),
                ProductEmbedding.is_listed.is_(True),
            )
            .order_by(distance_expr)
            .limit(top_k)
        )

        result = await session.execute(stmt)
        rows = result.all()

        results: list[SearchResult] = []
        for row in rows:
            score = float(row.score)
            if score < min_score:
                continue
            results.append(
                SearchResult(
                    product_id=row.product_id,
                    name=row.name,
                    description=row.description,
                    unit_price=float(row.unit_price),
                    score=score,
                    explanation=f"Cosine similarity {score:.4f} to query '{query}'",
                )
            )

        logger.info("semantic_search_completed", query=query, result_count=len(results))
        return results
