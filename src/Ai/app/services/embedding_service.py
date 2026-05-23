from __future__ import annotations

from typing import TYPE_CHECKING

import httpx
import structlog
from tenacity import retry, stop_after_attempt, wait_exponential

if TYPE_CHECKING:
    from app.settings import Settings

logger = structlog.get_logger()


class EmbeddingService:
    """Generates text embeddings via Voyage AI (Anthropic partner) API."""

    VOYAGE_API_URL = "https://api.voyageai.com/v1/embeddings"

    def __init__(self, settings: Settings) -> None:
        self._model = settings.embedding_model
        self._dimensions = settings.embedding_dimensions
        self._api_key = settings.anthropic_api_key
        self._client = httpx.AsyncClient(
            timeout=httpx.Timeout(60.0, connect=10.0),
            headers={
                "Authorization": f"Bearer {self._api_key}",
                "Content-Type": "application/json",
            },
        )

    async def close(self) -> None:
        await self._client.aclose()

    def _build_text(
        self,
        name: str,
        description: str,
        category_name: str,
        unit_price: float,
    ) -> str:
        parts = [f"Product: {name}"]
        if category_name:
            parts.append(f"Category: {category_name}")
        if description:
            parts.append(f"Description: {description}")
        parts.append(f"Price: ${unit_price:.2f}")
        return "\n".join(parts)

    @retry(stop=stop_after_attempt(3), wait=wait_exponential(multiplier=1, min=2, max=30))
    async def embed_text(self, text: str) -> list[float]:
        response = await self._client.post(
            self.VOYAGE_API_URL,
            json={
                "input": [text],
                "model": self._model,
            },
        )
        response.raise_for_status()
        data = response.json()
        embedding: list[float] = data["data"][0]["embedding"]
        return embedding

    async def embed_product(
        self,
        name: str,
        description: str,
        category_name: str,
        unit_price: float,
    ) -> list[float]:
        text = self._build_text(name, description, category_name, unit_price)
        logger.debug("embedding_product", name=name, text_length=len(text))
        return await self.embed_text(text)
