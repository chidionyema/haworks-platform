from __future__ import annotations

from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from app.services.content_service import (
    ContentGenerationRequest,
    ContentService,
    ContentType,
)
from app.settings import Settings


def _make_mock_anthropic() -> MagicMock:
    mock_client = MagicMock()
    mock_response = MagicMock()
    mock_response.content = [MagicMock(text="A beautifully crafted product description.")]
    mock_response.usage = MagicMock(input_tokens=100, output_tokens=50)
    mock_client.messages = MagicMock()
    mock_client.messages.create = AsyncMock(return_value=mock_response)
    return mock_client


class TestContentService:
    @pytest.mark.asyncio
    async def test_generate_product_description(self) -> None:
        mock_client = _make_mock_anthropic()
        with patch("app.services.content_service.AsyncAnthropic", return_value=mock_client):
            settings = Settings(anthropic_api_key="test-key", database_url="postgresql+asyncpg://x/y", rabbitmq_url="amqp://x")
            service = ContentService(settings)

            request = ContentGenerationRequest(
                content_type=ContentType.PRODUCT_DESCRIPTION,
                context={"name": "Artisan Candle", "material": "soy wax", "scent": "lavender"},
                tone="warm",
                max_words=150,
            )
            result = await service.generate(request)

            assert result.content == "A beautifully crafted product description."
            assert result.content_type == ContentType.PRODUCT_DESCRIPTION
            assert result.model_used == "claude-sonnet-4-5"
            assert result.token_count == 150

            mock_client.messages.create.assert_awaited_once()
            call_kwargs = mock_client.messages.create.call_args.kwargs
            assert call_kwargs["model"] == "claude-sonnet-4-5"
            assert call_kwargs["max_tokens"] == 1024
            assert len(call_kwargs["messages"]) == 1
            assert "Artisan Candle" in call_kwargs["messages"][0]["content"]

    @pytest.mark.asyncio
    async def test_generate_email_template(self) -> None:
        mock_client = _make_mock_anthropic()
        mock_client.messages.create.return_value.content[0].text = "Subject: Welcome!\n\nDear Customer..."
        with patch("app.services.content_service.AsyncAnthropic", return_value=mock_client):
            settings = Settings(anthropic_api_key="test-key", database_url="postgresql+asyncpg://x/y", rabbitmq_url="amqp://x")
            service = ContentService(settings)

            request = ContentGenerationRequest(
                content_type=ContentType.EMAIL_TEMPLATE,
                context={"campaign": "welcome", "discount": "10%"},
                tone="friendly",
                max_words=300,
            )
            result = await service.generate(request)

            assert result.content_type == ContentType.EMAIL_TEMPLATE
            assert "Welcome" in result.content

    @pytest.mark.asyncio
    async def test_generate_category_blurb(self) -> None:
        mock_client = _make_mock_anthropic()
        mock_client.messages.create.return_value.content[0].text = "Discover our handpicked collection."
        with patch("app.services.content_service.AsyncAnthropic", return_value=mock_client):
            settings = Settings(anthropic_api_key="test-key", database_url="postgresql+asyncpg://x/y", rabbitmq_url="amqp://x")
            service = ContentService(settings)

            request = ContentGenerationRequest(
                content_type=ContentType.CATEGORY_BLURB,
                context={"category": "Home & Garden", "item_count": 42},
                tone="professional",
                max_words=100,
            )
            result = await service.generate(request)

            assert result.content_type == ContentType.CATEGORY_BLURB
            assert result.content == "Discover our handpicked collection."
            assert result.token_count == 150

    @pytest.mark.asyncio
    async def test_generate_uses_correct_prompt_template(self) -> None:
        mock_client = _make_mock_anthropic()
        with patch("app.services.content_service.AsyncAnthropic", return_value=mock_client):
            settings = Settings(anthropic_api_key="test-key", database_url="postgresql+asyncpg://x/y", rabbitmq_url="amqp://x")
            service = ContentService(settings)

            request = ContentGenerationRequest(
                content_type=ContentType.PRODUCT_DESCRIPTION,
                context={"name": "Test Product"},
                tone="casual",
                max_words=50,
            )
            await service.generate(request)

            call_kwargs = mock_client.messages.create.call_args.kwargs
            prompt = call_kwargs["messages"][0]["content"]
            assert "product description" in prompt.lower()
            assert "casual" in prompt
            assert "50" in prompt
