from __future__ import annotations

import enum
from dataclasses import dataclass
from typing import Any

import structlog
from anthropic import AsyncAnthropic
from tenacity import retry, retry_if_exception, stop_after_attempt, wait_exponential

from app.settings import Settings

logger = structlog.get_logger()


class ContentType(str, enum.Enum):
    PRODUCT_DESCRIPTION = "product_description"
    EMAIL_TEMPLATE = "email_template"
    CATEGORY_BLURB = "category_blurb"


@dataclass(frozen=True)
class ContentGenerationRequest:
    content_type: ContentType
    context: dict[str, Any]
    tone: str = "professional"
    max_words: int = 200


@dataclass(frozen=True)
class ContentGenerationResponse:
    content: str
    content_type: ContentType
    model_used: str
    token_count: int


PROMPTS: dict[ContentType, str] = {
    ContentType.PRODUCT_DESCRIPTION: (
        "Write a compelling product description for an e-commerce listing.\n"
        "Product details: {context}\n"
        "Tone: {tone}\n"
        "Maximum words: {max_words}\n"
        "Focus on benefits, features, and why a customer should buy this product."
    ),
    ContentType.EMAIL_TEMPLATE: (
        "Write a marketing email template for an e-commerce platform.\n"
        "Context: {context}\n"
        "Tone: {tone}\n"
        "Maximum words: {max_words}\n"
        "Include a subject line, greeting, body, and call-to-action."
    ),
    ContentType.CATEGORY_BLURB: (
        "Write a short category description for an e-commerce category page.\n"
        "Category details: {context}\n"
        "Tone: {tone}\n"
        "Maximum words: {max_words}\n"
        "Make it SEO-friendly and enticing for shoppers."
    ),
}


def _is_transient(exc: BaseException) -> bool:
    """Only retry on transient server errors, not 400/401."""
    from anthropic import APIStatusError
    return isinstance(exc, APIStatusError) and exc.status_code >= 500


class ContentService:
    def __init__(self, settings: Settings) -> None:
        self._client = AsyncAnthropic(api_key=settings.anthropic_api_key)
        self._model = settings.llm_model_fast

    @retry(
        stop=stop_after_attempt(3),
        wait=wait_exponential(multiplier=1, min=1, max=10),
        retry=retry_if_exception(_is_transient),
        reraise=True,
    )
    async def generate(self, request: ContentGenerationRequest) -> ContentGenerationResponse:
        prompt_template = PROMPTS[request.content_type]
        prompt = prompt_template.format(
            context=request.context,
            tone=request.tone,
            max_words=request.max_words,
        )

        response = await self._client.messages.create(
            model=self._model,
            max_tokens=1024,
            messages=[{"role": "user", "content": prompt}],
        )

        if not response.content:
            raise ValueError("Anthropic returned empty content")

        content_text = response.content[0].text
        token_count = response.usage.input_tokens + response.usage.output_tokens

        logger.info(
            "content_generated",
            content_type=request.content_type.value,
            tokens=token_count,
        )

        return ContentGenerationResponse(
            content=content_text,
            content_type=request.content_type,
            model_used=self._model,
            token_count=token_count,
        )
