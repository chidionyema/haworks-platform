from __future__ import annotations

import uuid
from datetime import datetime
from typing import Any, Generic, TypeVar

from pydantic import BaseModel, Field

T = TypeVar("T", bound=BaseModel)


class MassTransitEnvelope(BaseModel, Generic[T]):
    model_config = {"populate_by_name": True}

    message_id: uuid.UUID = Field(alias="messageId")
    conversation_id: uuid.UUID | None = Field(default=None, alias="conversationId")
    source_address: str | None = Field(default=None, alias="sourceAddress")
    destination_address: str | None = Field(default=None, alias="destinationAddress")
    message_type: list[str] = Field(default_factory=list, alias="messageType")
    message: T
    headers: dict[str, Any] = Field(default_factory=dict)
    sent_time: datetime | None = Field(default=None, alias="sentTime")


class OrderCompletedMessage(BaseModel):
    model_config = {"populate_by_name": True}

    order_id: uuid.UUID = Field(alias="orderId")
    customer_id: str = Field(alias="customerId")
    total_amount: float = Field(alias="totalAmount")
    completed_at: datetime = Field(alias="completedAt")


class ProductCacheInvalidatedMessage(BaseModel):
    model_config = {"populate_by_name": True}

    product_id: uuid.UUID = Field(alias="productId")
    version: int = Field(default=0)
