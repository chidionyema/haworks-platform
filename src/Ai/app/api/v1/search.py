from __future__ import annotations

import uuid

from fastapi import APIRouter, Depends
from pydantic import BaseModel, Field
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.deps import get_db_session, get_rag_service
from app.services.rag_service import RagService

router = APIRouter(prefix="/search", tags=["search"])


class SearchRequest(BaseModel):
    query: str = Field(min_length=1, max_length=1000)
    top_k: int = Field(default=10, ge=1, le=100)
    min_score: float = Field(default=0.0, ge=0.0, le=1.0)


class SearchResultItem(BaseModel):
    product_id: uuid.UUID
    name: str
    description: str
    unit_price: float
    score: float
    explanation: str


class SearchResponse(BaseModel):
    results: list[SearchResultItem]
    query: str
    total: int


@router.post("", response_model=SearchResponse)
async def semantic_search(
    request: SearchRequest,
    session: AsyncSession = Depends(get_db_session),
    rag_service: RagService = Depends(get_rag_service),
) -> SearchResponse:
    results = await rag_service.search(
        session=session,
        query=request.query,
        top_k=request.top_k,
        min_score=request.min_score,
    )
    return SearchResponse(
        results=[
            SearchResultItem(
                product_id=r.product_id,
                name=r.name,
                description=r.description,
                unit_price=r.unit_price,
                score=r.score,
                explanation=r.explanation,
            )
            for r in results
        ],
        query=request.query,
        total=len(results),
    )
