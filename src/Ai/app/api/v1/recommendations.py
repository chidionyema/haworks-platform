from __future__ import annotations

import uuid

from fastapi import APIRouter, Depends, Query
from pydantic import BaseModel
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.deps import get_db_session, get_recommendation_service
from app.services.recommendation_service import RecommendationService

router = APIRouter(prefix="/recommendations", tags=["recommendations"])


class RecommendationItem(BaseModel):
    product_id: uuid.UUID
    name: str
    description: str
    unit_price: float
    score: float


class RecommendationsResponse(BaseModel):
    user_id: str
    recommendations: list[RecommendationItem]
    total: int


@router.get("/{user_id}", response_model=RecommendationsResponse)
async def get_recommendations(
    user_id: str,
    top_k: int = Query(default=10, ge=1, le=50),
    session: AsyncSession = Depends(get_db_session),
    recommendation_service: RecommendationService = Depends(get_recommendation_service),
) -> RecommendationsResponse:
    results = await recommendation_service.get_recommendations(
        session=session,
        user_id=user_id,
        top_k=top_k,
    )
    return RecommendationsResponse(
        user_id=user_id,
        recommendations=[
            RecommendationItem(
                product_id=r.product_id,
                name=r.name,
                description=r.description,
                unit_price=r.unit_price,
                score=r.score,
            )
            for r in results
        ],
        total=len(results),
    )
