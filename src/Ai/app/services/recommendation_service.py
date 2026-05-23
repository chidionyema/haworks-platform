from __future__ import annotations

import uuid
from dataclasses import dataclass
from datetime import UTC, datetime
from typing import TYPE_CHECKING

import structlog
from pgvector.sqlalchemy import Vector
from sqlalchemy import cast, select

from app.domain.models import ProductEmbedding, UserPreference

if TYPE_CHECKING:
    from sqlalchemy.ext.asyncio import AsyncSession

logger = structlog.get_logger()


@dataclass(frozen=True)
class RecommendedProduct:
    product_id: uuid.UUID
    name: str
    description: str
    unit_price: float
    score: float


class RecommendationService:
    async def record_order_signal(
        self,
        session: AsyncSession,
        customer_id: str,
        order_id: uuid.UUID,
        total_amount: float,
        completed_at: datetime,
    ) -> None:
        result = await session.execute(
            select(UserPreference).where(UserPreference.customer_id == customer_id)
        )
        pref = result.scalar_one_or_none()

        if pref is None:
            pref = UserPreference(
                customer_id=customer_id,
                order_count=1,
                total_spent=total_amount,
                updated_at=datetime.now(UTC),
            )
            session.add(pref)
        else:
            pref.order_count += 1
            pref.total_spent = float(pref.total_spent) + total_amount
            pref.updated_at = datetime.now(UTC)

        await session.flush()
        logger.info(
            "order_signal_recorded",
            customer_id=customer_id,
            order_id=str(order_id),
            order_count=pref.order_count,
        )

    async def update_preference_vector(
        self,
        session: AsyncSession,
        customer_id: str,
        product_embedding: list[float],
    ) -> None:
        result = await session.execute(
            select(UserPreference).where(UserPreference.customer_id == customer_id)
        )
        pref = result.scalar_one_or_none()
        if pref is None:
            return

        if pref.preference_vector is None:
            pref.preference_vector = product_embedding
        else:
            current = list(pref.preference_vector)
            alpha = 1.0 / pref.order_count
            blended = [
                (1 - alpha) * c + alpha * n
                for c, n in zip(current, product_embedding, strict=True)
            ]
            pref.preference_vector = blended

        pref.updated_at = datetime.now(UTC)
        await session.flush()

    async def get_recommendations(
        self,
        session: AsyncSession,
        user_id: str,
        top_k: int = 10,
    ) -> list[RecommendedProduct]:
        result = await session.execute(
            select(UserPreference).where(UserPreference.customer_id == user_id)
        )
        pref = result.scalar_one_or_none()

        if pref is None or pref.preference_vector is None:
            stmt = (
                select(
                    ProductEmbedding.product_id,
                    ProductEmbedding.name,
                    ProductEmbedding.description,
                    ProductEmbedding.unit_price,
                )
                .where(
                    ProductEmbedding.is_listed.is_(True),
                    ProductEmbedding.is_in_stock.is_(True),
                )
                .order_by(ProductEmbedding.unit_price.desc())
                .limit(top_k)
            )
            rows = (await session.execute(stmt)).all()
            return [
                RecommendedProduct(
                    product_id=row.product_id,
                    name=row.name,
                    description=row.description,
                    unit_price=float(row.unit_price),
                    score=0.0,
                )
                for row in rows
            ]

        pref_vector = list(pref.preference_vector)
        distance_expr = ProductEmbedding.embedding.cosine_distance(cast(pref_vector, Vector(1024)))

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
                ProductEmbedding.is_in_stock.is_(True),
            )
            .order_by(distance_expr)
            .limit(top_k)
        )

        rows = (await session.execute(stmt)).all()
        results = [
            RecommendedProduct(
                product_id=row.product_id,
                name=row.name,
                description=row.description,
                unit_price=float(row.unit_price),
                score=float(row.score),
            )
            for row in rows
        ]

        logger.info("recommendations_generated", user_id=user_id, count=len(results))
        return results
