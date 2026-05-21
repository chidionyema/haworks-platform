# Unconsumed Events Backlog

Date: 2026-05-21
Source: Saga & Messaging Production Readiness Audit (M9)

## FIXED — Media Pipeline (was broken)

- [x] MediaScanPassedEvent → MediaScanPassedConsumer (moves file from quarantine to public, marks Active)
- [x] MediaScanFailedEvent → MediaScanFailedConsumer (rejects record, deletes quarantine file)

## Backlog — Merchant Events (planned for Notifications + Search)

| Event | Publisher | Intended Consumer | Priority |
|-------|----------|-------------------|----------|
| MerchantCreatedEvent | CreateMerchantCommand | Notifications (welcome email), Search (index) | P2 |
| MerchantActivatedEvent | ApproveMerchantCommand | Notifications (activation email) | P2 |
| MerchantSuspendedEvent | SuspendMerchantCommand | Notifications (suspension notice) | P2 |
| MerchantDeactivatedEvent | DeactivateMerchantCommand | Notifications (deactivation email) | P2 |

## Backlog — Shipping Events (planned for Orders + Notifications)

| Event | Publisher | Intended Consumer | Priority |
|-------|----------|-------------------|----------|
| ShipmentCreatedEvent | ShipmentsController | Orders (attach tracking number) | P1 |
| ShipmentDeliveredEvent | ShipmentsController | Orders (mark delivered), Notifications (delivery email) | P1 |
| ShipmentExceptionEvent | ShipmentsController | Orders (flag exception), Notifications (alert) | P1 |

## Backlog — Infrastructure / Ops Alerting

| Event | Publisher | Intended Consumer | Priority |
|-------|----------|-------------------|----------|
| RotationFailedEvent | LeaseWatcherJob | Notifications (ops alert) | P1 |
| CredentialRotatedEvent | LeaseWatcherJob | Audit (trail) — already captured by AuditConsumer<T> | P3 |
| CertificateRotatedEvent | LeaseWatcherJob | Audit (trail) — already captured by AuditConsumer<T> | P3 |
| StripeKeyRotationStartedEvent | RotateStripeKeyCommand | Audit (trail) — already captured by AuditConsumer<T> | P3 |
| StripeKeyRevocationFailedEvent | RevokeOldStripeKeyJob | Notifications (ops alert) | P1 |

## Backlog — Other

| Event | Publisher | Intended Consumer | Priority |
|-------|----------|-------------------|----------|
| StockReleasedEvent | StockReleaseRequestedConsumer | Informational only; no consumer needed | P3 |
| MediaProcessingCompletedEvent | ProcessMediaConsumer | Notify uploader (Notifications), update CDN | P2 |
| MediaDeletedEvent | DeleteMedia | Clean CDN cache, Search deindex | P2 |
| TranslationUpdatedEvent | UpsertTranslationCommand | Search reindex of translated content | P3 |

## Notes

- MassTransit pub-sub discards messages with no bound consumer (no queue accumulation)
- Audit service's AuditConsumer<T> consumes ALL IDomainEvent types via reflection — so Credential/Certificate/StripeKey events ARE captured for audit trail
- Shipping events (P1) should be wired when Orders service adds shipment tracking
- Ops alerting events (P1) should route to Notifications service's existing email gateway
