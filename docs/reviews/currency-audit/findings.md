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
| PRC-01 | `src/Pricing/Pricing.Application/Consumers/PricingRequestedConsumer.cs:64` | `SubtotalCents = (long)Math.Round(Subtotal*100m,0)` | 2 | OPEN |
| PRC-02 | `src/Pricing/Pricing.Application/Consumers/PricingRequestedConsumer.cs:65` | `TaxCents = (long)Math.Round(TaxAmount*100m,0)` | 2 | OPEN |
| PRC-03 | `src/Pricing/Pricing.Application/Consumers/PricingRequestedConsumer.cs:66` | `TotalCents = (long)Math.Round(Total*100m,0)` | 2 | OPEN |
| SHP-01 | `src/Shipping/Shipping.Api/Controllers/ShipmentsController.cs:52` | `(long)Math.Round(result.Amount*100m,...)` EasyPost rate | 2 | OPEN |
| BFF-04 | `src/BffWeb/BffWeb.Api/Controllers/CheckoutController.cs:56` | `(long)Math.Round(body.TotalAmount*100m,...)` | 3 | OPEN |
| BFF-05 | `src/BffWeb/BffWeb.Api/Controllers/CheckoutController.cs:95` | `(long)Math.Round(i.UnitPrice*100m,...)` | 3 | OPEN |
| BFF-07 | `src/BffWeb/BffWeb.Api/Controllers/DemoController.cs:237` | `i.UnitPriceCents / 100m` | 3 | OPEN |
| BFF-08 | `src/BffWeb/BffWeb.Api/Controllers/DemoController.cs:1380` | `request.RefundAmountCents / 100m` | 3 | OPEN |
| SCH-03 | `src/Search/Search.Application/Consumers/IndexableEntityChangedConsumer.cs:156` | `(decimal)priceCents / 100m` | 3 | OPEN |
| CHK-fixed | `src/CheckoutOrchestrator/.../CheckoutsController.cs` | was `*100m`; now `Money.TryFromMajorUnits` | 0 | FIXED |

## 🟠 Anti-pattern #3 — currency accepted but never validated at boundary

| ID | File:line | Detail | Batch | Status |
|----|-----------|--------|-------|--------|
| PAY-25 | `src/Payments/Payments.Api/Controllers/RefundsController.cs:62` | `Currency` on CreateRefundRequest unvalidated | 1 | OPEN |
| PAY-12 | `src/Payments/Payments.Infrastructure/PayPal/PayPalSubscriptionManager.cs:187` | webhook `currency` metadata unvalidated | 1 | OPEN |
| PYT-05 | `src/Payouts/Payouts.Api/Controllers/LedgerController.cs:65` | query `currency` → LedgerAccount.Create unvalidated | 4 | OPEN |
| ORD-03 | `src/Orders/Orders.Application/Commands/CreateOrderCommand.cs:16` | `string Currency` unvalidated | 4 | OPEN |

## 🟡 Anti-pattern #4 — un-migrated decimal money fields / missing currency

| ID | File:line | Detail | Batch | Status |
|----|-----------|--------|-------|--------|
| ORD-02 | `src/Orders/Orders.Domain/Subscription.cs:82` | `decimal Price` on SubscriptionPlan, no currency | 4 | OPEN |
| ORD-04 | `src/Orders/Orders.Api/Controllers/SubscriptionsController.cs:84` | `decimal Amount` on request | 4 | OPEN |
| SCH-01 | `src/Search/Search.Application/Models/ProductSearchDocument.cs:21` | `decimal UnitPrice`, no currency | 3 | OPEN |
| SCH-02 | `src/Search/Search.Application/Indexing/ProductSearchDocumentProjector.cs:15` | `decimal unitPrice` param | 3 | OPEN |
| SCH-04 | `src/Search/.../CatalogProductDto` | `long UnitPriceCents` but **no CurrencyCode** field | 3 | OPEN |
| BFF-01 | `src/BffWeb/BffWeb.Api/Controllers/CheckoutController.cs:119` | `decimal TotalAmount` on CheckoutRequest | 3 | OPEN |
| BFF-03 | `src/BffWeb/BffWeb.Api/Controllers/CheckoutController.cs:130` | `decimal UnitPrice` on CheckoutLineItem | 3 | OPEN |

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
| PYT-01 | `src/Payouts/Payouts.Api/Controllers/LedgerController.cs:21` (`= "USD"` param) | 4 | OPEN |
| PYT-02 | `src/Payouts/Payouts.Api/Controllers/LedgerController.cs:52` (const) | 4 | OPEN |
| ORD-01 | `src/Orders/Orders.Domain/Order.cs:45` (`= "USD"`) | 4 | OPEN |
| CAT-01 | `src/Catalog/Catalog.Api/Controllers/ProductsController.cs:76` | 5 | OPEN |
| CAT-02 | `src/Catalog/Catalog.Api/Controllers/ProductsController.cs:92` | 5 | OPEN |
| CAT-03 | `src/Catalog/Catalog.Application/Commands/ReserveStockCommand.cs:97` (`?? "USD"`) | 5 | OPEN |
| PRC-04 | `src/Pricing/Pricing.Domain/Entities/PriceCalculationLog.cs:20` (`= "USD"`) | 2 | OPEN |
| PRC-05 | `src/Pricing/Pricing.Application/Models/CatalogProductDto.cs:11` (`= "USD"`) | 2 | OPEN |
| SHP-02 | `src/Shipping/Shipping.Api/Domain/Shipment.cs:26` (`= "USD"`) | 2 | OPEN |
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
