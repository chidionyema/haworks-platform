from __future__ import annotations

import json
import uuid
from datetime import datetime

import pytest

from app.messaging.masstransit import (
    MassTransitEnvelope,
    OrderCompletedMessage,
    ProductCacheInvalidatedMessage,
)


SAMPLE_ORDER_COMPLETED_JSON = json.dumps({
    "messageId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "conversationId": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
    "sourceAddress": "rabbitmq://localhost/order-svc",
    "destinationAddress": "rabbitmq://localhost/ai-svc:order-completed",
    "messageType": [
        "urn:message:HaWorks.BuildingBlocks.SharedContracts:OrderCompletedEvent"
    ],
    "message": {
        "orderId": "c3d4e5f6-a7b8-9012-cdef-123456789012",
        "customerId": "user-42",
        "totalAmount": 149.99,
        "completedAt": "2026-05-22T10:30:00Z"
    },
    "headers": {"MT-Activity-Id": "some-trace-id"},
    "sentTime": "2026-05-22T10:30:01Z"
})

SAMPLE_PRODUCT_INVALIDATED_JSON = json.dumps({
    "messageId": "d4e5f6a7-b8c9-0123-defa-234567890123",
    "messageType": [
        "urn:message:HaWorks.BuildingBlocks.SharedContracts:ProductCacheInvalidatedEvent"
    ],
    "message": {
        "productId": "e5f6a7b8-c9d0-1234-efab-345678901234",
        "version": 5
    },
    "headers": {},
    "sentTime": "2026-05-22T11:00:00Z"
})


class TestMassTransitEnvelope:
    def test_deserialize_order_completed(self) -> None:
        data = json.loads(SAMPLE_ORDER_COMPLETED_JSON)
        envelope = MassTransitEnvelope[OrderCompletedMessage].model_validate(data)

        assert envelope.message_id == uuid.UUID("a1b2c3d4-e5f6-7890-abcd-ef1234567890")
        assert envelope.conversation_id == uuid.UUID("b2c3d4e5-f6a7-8901-bcde-f12345678901")
        assert envelope.source_address == "rabbitmq://localhost/order-svc"
        assert len(envelope.message_type) == 1
        assert envelope.message_type[0].endswith(":OrderCompletedEvent")
        assert envelope.headers.get("MT-Activity-Id") == "some-trace-id"
        assert envelope.sent_time is not None

    def test_deserialize_order_completed_message(self) -> None:
        data = json.loads(SAMPLE_ORDER_COMPLETED_JSON)
        msg = OrderCompletedMessage.model_validate(data["message"])

        assert msg.order_id == uuid.UUID("c3d4e5f6-a7b8-9012-cdef-123456789012")
        assert msg.customer_id == "user-42"
        assert msg.total_amount == 149.99
        assert isinstance(msg.completed_at, datetime)

    def test_deserialize_product_invalidated(self) -> None:
        data = json.loads(SAMPLE_PRODUCT_INVALIDATED_JSON)
        envelope = MassTransitEnvelope[ProductCacheInvalidatedMessage].model_validate(data)

        assert envelope.message_id == uuid.UUID("d4e5f6a7-b8c9-0123-defa-234567890123")
        assert len(envelope.message_type) == 1
        assert envelope.message_type[0].endswith(":ProductCacheInvalidatedEvent")

    def test_deserialize_product_invalidated_message(self) -> None:
        data = json.loads(SAMPLE_PRODUCT_INVALIDATED_JSON)
        msg = ProductCacheInvalidatedMessage.model_validate(data["message"])

        assert msg.product_id == uuid.UUID("e5f6a7b8-c9d0-1234-efab-345678901234")
        assert msg.version == 5

    def test_envelope_with_missing_optional_fields(self) -> None:
        minimal = {
            "messageId": "f6a7b8c9-d0e1-2345-fgab-456789012345",
            "messageType": ["urn:message:Test:SomeEvent"],
            "message": {"productId": "a1a1a1a1-b2b2-c3c3-d4d4-e5e5e5e5e5e5", "version": 0},
            "headers": {},
        }
        envelope = MassTransitEnvelope[ProductCacheInvalidatedMessage].model_validate(minimal)

        assert envelope.conversation_id is None
        assert envelope.source_address is None
        assert envelope.sent_time is None
        assert envelope.destination_address is None

    def test_envelope_preserves_all_message_types(self) -> None:
        data = {
            "messageId": "00000000-0000-0000-0000-000000000001",
            "messageType": [
                "urn:message:Namespace:IOrderCompletedEvent",
                "urn:message:Namespace:OrderCompletedEvent",
            ],
            "message": {
                "orderId": "00000000-0000-0000-0000-000000000002",
                "customerId": "user-1",
                "totalAmount": 10.0,
                "completedAt": "2026-01-01T00:00:00Z",
            },
            "headers": {},
        }
        envelope = MassTransitEnvelope[OrderCompletedMessage].model_validate(data)
        assert len(envelope.message_type) == 2
