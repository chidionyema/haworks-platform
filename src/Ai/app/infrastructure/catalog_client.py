from __future__ import annotations

from typing import Any

import httpx
import structlog
from tenacity import retry, stop_after_attempt, wait_exponential

logger = structlog.get_logger()


class CatalogClient:
    def __init__(self, base_url: str) -> None:
        self._base_url = base_url.rstrip("/")
        self._client = httpx.AsyncClient(
            base_url=self._base_url,
            timeout=httpx.Timeout(30.0, connect=10.0),
        )

    async def close(self) -> None:
        await self._client.aclose()

    @retry(stop=stop_after_attempt(3), wait=wait_exponential(multiplier=1, min=1, max=10))
    async def get_product(self, product_id: str) -> dict[str, Any] | None:
        response = await self._client.get(f"/api/v1/products/{product_id}")
        if response.status_code == 404:
            return None
        response.raise_for_status()
        return response.json()

    @retry(stop=stop_after_attempt(3), wait=wait_exponential(multiplier=1, min=1, max=10))
    async def list_products(self, skip: int = 0, take: int = 50) -> list[dict[str, Any]]:
        response = await self._client.get(
            "/api/v1/products",
            params={"skip": skip, "take": take},
        )
        response.raise_for_status()
        return response.json()
