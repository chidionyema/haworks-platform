# Currency Audit — Findings Register

Status legend: `OPEN` · `IN PROGRESS` · `FIXED` · `WONTFIX` (with rationale).
Anti-pattern `#` refers to the catalogue in [`README.md`](./README.md).
Line numbers are as of the 2026-05-30 audit; verify before editing.

## 🔴 Anti-pattern #1 — hardcoded ×100 / ÷100 conversions (correctness bugs)

| ID | File:line | Detail | Batch | Status |
|----|-----------|--------|-------|--------|
| PAY-03 | `src/Payments/Payments.Application/Webhooks/PaymentAmountMismatchHandler.cs:61` | `Math.Round(actualPaid * 100m,0)` | 1 | OPEN |
| PAY-04 | `src/Payments/Payments.Application/Webhooks/PaymentAmountMismatchHandler.cs:62` | `Math.Round(expectedTotal * 100m,0)` | 1 | OPEN |
| PAY-05 | `src/Payments/Payments.Application/Webhooks/PaymentAmountMismatchHandler.cs:63` | `Math.Round(difference * 100m,0)` | 1 | OPEN |
| PAY-06 | `src/Payments/Payments.Infrastructure/PayPal/PayPalWebhookProcessor.cs:238` | `Math.Round(decimal.Parse(amount)*100m,...)` provider amount | 1 | OPEN |
| PAY-07 | `src/Payments/Payments.Infrastructure/PayPal/PayPalPaymentProcessor.cs:83` | `actualPaidCents / 100m` | 1 | OPEN |
| PAY-08 | `src/Payments/Payments.Infrastructure/PayPal/PayPalPaymentProcessor.cs:83` | `expectedTotalCents / 100m` | 1 | OPEN |
| PAY-15 | `src/Payments/Payments.Infrastructure/Stripe/StripePaymentProcessor.cs:114` | `actualPaidCents/100m` & `expectedTotalCents/100m` | 1 | OPEN |
| PRC-01 | `PricingRequestedConsumer.cs:64` | now `Money.FromMajorUnits(result.Subtotal, result.Currency).MinorUnits` | 2 | FIXED |
| PRC-02 | `PricingRequestedConsumer.cs:65` | now `Money.FromMajorUnits(result.TaxAmount, result.Currency).MinorUnits` | 2 | FIXED |
| PRC-03 | `PricingRequestedConsumer.cs:66` | now `Money.FromMajorUnits(result.Total, result.Currency).MinorUnits` | 2 | FIXED |
| SHP-01 | `ShipmentsController.cs:52` | now `Money.FromMajorUnits(result.Amount, result.Currency).MinorUnits` | 2 | FIXED |
| BFF-04 | `CheckoutController.cs:56` | activity tag now uses `Money.FromMajorUnits` | 3 | FIXED |
| BFF-05 | `CheckoutController.cs:95` | `unitPriceCents` now uses `Money.FromMajorUnits` | 3 | FIXED |
| BFF-07 | `DemoController.cs:237` | total calculation now uses `new Money(...).ToMajorUnits()` | 3 | FIXED |
| BFF-08 | `DemoController.cs:1380` | refund amount now uses `new Money(...).ToMajorUnits()` | 3 | FIXED |
| SCH-03 | `IndexableEntityChangedConsumer.cs:156` | removed `/ 100m`, document now stores `UnitPriceCents` | 3 | FIXED |
| CHK-fixed | `src/CheckoutOrchestrator/.../CheckoutsController.cs` | was `*100m`; now `Money.TryFromMajorUnits` | 0 | FIXED |

## 🟠 Anti-pattern #3 — currency accepted but never validated at boundary

| ID | File:line | Detail | Batch | Status |
|----|-----------|--------|-------|--------|
| PAY-25 | `src/Payments/Payments.Api/Controllers/RefundsController.cs:62` | `Currency` on CreateRefundRequest unvalidated | 1 | OPEN |
| PAY-12 | `src/Payments/Payments.Infrastructure/PayPal/PayPalSubscriptionManager.cs:187` | webhook `currency` metadata unvalidated | 1 | OPEN |
| PYT-05 | `LedgerAccount.cs:33` | now validates `currency` in `Create` | 4 | FIXED |
| ORD-03 | `CreateOrderCommandValidator.cs:13` | now uses `.MustBeValidCurrency()` | 4 | FIXED |

## 🟡 Anti-pattern #4 — un-migrated decimal money fields / missing currency

| ID | File:line | Detail | Batch | Status |
|----|-----------|--------|-------|--------|
| ORD-02 | `src/Payments/Payments.Domain/Subscription.cs:82` | `Price` → `PriceCents` (long) + `Currency` added | 4 | FIXED |
| ORD-04 | `src/Payments/Payments.Api/Controllers/SubscriptionsController.cs:84` | added `Currency`; converted `decimal Amount` to cents | 4 | FIXED |
| SCH-01 | `ProductSearchDocument.cs:21` | UnitPrice → UnitPriceCents + CurrencyCode | 3 | FIXED |
| SCH-02 | `ProductSearchDocumentProjector.cs:15` | unitPrice → unitPriceCents + currencyCode | 3 | FIXED |
| SCH-04 | `CatalogProductDto.cs` | added `Currency` field | 3 | FIXED |
| BFF-01 | `CheckoutController.cs:119` | `TotalAmount` on CheckoutRequest (keep decimal, but removed USD default) | 3 | FIXED |
| BFF-03 | `CheckoutController.cs:130` | `UnitPrice` on CheckoutLineItem (keep decimal, but removed USD default) | 3 | FIXED |

## 🟡 Anti-pattern #2 — silent "USD" defaults

| ID | File:line | Batch | Status |
|----|-----------|-------|--------|
| PAY-01 | `src/Payments/Payments.Domain/Payment.cs:44` (`= "USD"`) | 5 | OPEN |
| PAY-02 | `src/Payments/Payments.Domain/RefundSagaState.cs:15` (`= "USD"`) | 5 | OPEN |
| PAY-09 | `src/Payments/Payments.Infrastructure/PayPal/PayPalModels.cs:42` | 5 | OPEN |
| PAY-10 | `src/Payments/Payments.Infrastructure/PayPal/PayPalModels.cs:206` | 5 | OPEN |
| PAY-11 | `src/Payments/Payments.Infrastructure/PayPal/PayPalSubscriptionManager.cs:28` (const) | 5 | OPEN |
| PAY-13 | `src/Payments/Payments.Infrastructure/PayPal/PayPalCheckoutService.cs:24` (const) | 5 | OPEN |
| PAY-14 | `src/Payments/Payments.Infrastructure/PayPal/PayPalRefundService.cs:27` (const) | 5 | OPEN |
| PAY-16 | `src/Payments/Payments.Infrastructure/Stripe/StripeCheckoutSessionService.cs:16` (const) | 5 | OPEN |
| PAY-17 | `src/Payments/Payments.Infrastructure/Stripe/StripeWebhookProcessor.cs:21` (const) | 5 | OPEN |
| PAY-18 | `src/Payments/Payments.Infrastructure/Stripe/StripeSubscriptionManager.cs:27` (const) | 5 | OPEN |
| PAY-19 | `src/Payments/Payments.Api/Controllers/AdminController.cs:31` (const) | 5 | OPEN |
| PAY-20 | `src/Payments/Payments.Application/Consumers/ProviderRefundCancellationConsumer.cs:54` (`?? "USD"`) | 5 | OPEN |
| PAY-21 | `src/Payments/Payments.Application/Consumers/ProviderRefundCancellationConsumer.cs:88` (`?? "USD"`) | 5 | OPEN |
| PAY-22 | `src/Payments/Payments.Application/Interfaces/ICheckoutSessionService.cs:43` | 5 | OPEN |
| PAY-23 | `src/Payments/Payments.Application/Interfaces/ICheckoutSessionService.cs:58` | 5 | OPEN |
| PAY-24 | `src/Payments/Payments.Application/Interfaces/ISubscriptionManager.cs:18` | 5 | OPEN |
| PYT-01 | `LedgerController.cs:21` | removed silent "USD" default; added manual validation | 4 | FIXED |
| PYT-02 | `LedgerController.cs:52` | removed `DefaultCurrency` const | 4 | FIXED |
| ORD-01 | `Order.cs:45` | removed silent "USD" default | 4 | FIXED |
| CAT-01 | `src/Catalog/Catalog.Api/Controllers/ProductsController.cs:76` | 5 | OPEN |
| CAT-02 | `src/Catalog/Catalog.Api/Controllers/ProductsController.cs:92` | 5 | OPEN |
| CAT-03 | `src/Catalog/Catalog.Application/Commands/ReserveStockCommand.cs:97` (`?? "USD"`) | 5 | OPEN |
| PRC-04 | `PriceCalculationLog.cs:20` | removed silent "USD" default | 2 | FIXED |
| PRC-05 | `CatalogProductDto.cs:11` | removed silent "USD" default | 2 | FIXED |
| SHP-02 | `Shipment.cs:26` | removed silent "USD" default | 2 | FIXED |
| BFF-02 | `src/BffWeb/BffWeb.Api/Controllers/CheckoutController.cs:120` (`= "USD"`) | 3 | OPEN |
| BFF-06 | `src/BffWeb/BffWeb.Api/Controllers/CheckoutController.cs:96` (`?? "USD"`) | 3 | OPEN |
| CHK-01 | `src/CheckoutOrchestrator/.../CheckoutSagaState.cs:38` (`= "USD"`) | 0 | OPEN |
| CHK-02 | `src/CheckoutOrchestrator/.../CheckoutsController.cs` boundary default→USD | 0 | REVIEW (by-design fallback for un-supplied currency; revisit if multi-currency goes live) |

## WONTFIX / false positives

| ID | File:line | Rationale |
|----|-----------|-----------|
| PYT-03 | `src/Payouts/Payouts.Application/Ledger/Services/LedgerService.cs:62` | `amountCents * commissionRate / 100m` is a **percentage** (basis-point) calc, not a major↔minor conversion. Not a currency bug. (Sub-cent precision is a separate ledger concern.) |
| PYT-04 | `src/Payouts/Payouts.Application/Ledger/Services/LedgerService.cs:115` | Same as PYT-03. |
| MNY-tbl | `src/BuildingBlocks/Common/Money.cs:15` | HWK035 "hardcoded JPY" flags the exponent lookup **table itself** — correct by definition. Suppress/annotate. |

## Tally

- 🔴 #1 conversion bugs: **16** (1 fixed) → batches 1–3
- 🟠 #3 unvalidated currency: **4** → batches 1, 4
- 🟡 #4 un-migrated decimal/missing currency: **7** → batches 3, 4
- 🟡 #2 silent USD defaults: **29** → batches 2–5
- WONTFIX: 3

**Total tracked: 56 findings across 9 services + Contracts.**
</content>
