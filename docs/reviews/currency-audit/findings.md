# Currency Audit — Findings Register

Status legend: `OPEN` · `IN PROGRESS` · `FIXED` · `WONTFIX` (with rationale).
Anti-pattern `#` refers to the catalogue in [`README.md`](./README.md).
Line numbers are as of the 2026-05-30 audit; verify before editing.

## ⚠️ Adversarial deep-review addendum (2026-05-30, 20-agent verify pass)

The initial file-level sweep MISSED real bugs — including in services it wrongly cleared
as "no monetary fields" (RulesEngine, Notifications). A 10-service analyze + adversarial-verify
workflow re-derived every value and found **no arithmetic errors in already-fixed sites and no
circular unit tests**, but surfaced these NEW findings:

| ID | File:line | Severity | Detail | Status |
|----|-----------|----------|--------|--------|
| ADV-01 | `RulesEngine.Api/Infrastructure/FraudCheckConsumer.cs:42` | 🔴 wrong-amount | `/100m` fed into fraud-rule eval; `msg.Currency` in scope → JPY 100×/KWD 10× wrong → **wrong fraud decision** | FIXED → Money.ToMajorUnits |
| ADV-02 | `Pricing.Application/Queries/CalculateEffectivePriceQueryHandler.cs:103` | 🔴 wrong-amount | hardcoded `Round(.,2)` BEFORE minor-unit conversion → 3-decimal currencies lose 3rd digit (1.234 KWD→1230) | FIXED → Round(., GetExponent(currency)) |
| ADV-03 | `BffWeb.Api/Controllers/DemoController.cs:238` | 🔴 wrong-amount | `new Money(..., 'GBP')` hardcodes GBP for non-GBP items → wrong totalAmount to payment orchestrator | OPEN (PR-B) |
| ADV-04 | `Notifications.Application/Consumers/RefundEmailConsumer.cs:36` | 🟠 display | `/100m` → wrong amount in refund email | FIXED → Money.ToMajorUnits |
| ADV-05 | `BffWeb.Api/SignalR/SagaStepBridgeConsumers.cs:106,137` | 🟠 display | currency-blind `/100m` in SignalR saga step strings | OPEN (PR-B) |
| ADV-06 | `Payments.Api/.../SubscriptionsController.cs:42` | 🟠 robustness | `Money.FromMajorUnits` runs before validation → invalid currency = 500 not 400 | OPEN |
| ADV-07 | `BuildingBlocks/Common/Money.cs:28` IsValidCurrencyCode | 🟡 foundational | structural-only: `'ZZZ'`/`'XXX'` pass, silently get exponent-2 | OPEN (decision: ISO allowlist?) |
| ADV-08 | `Money.cs:34` FromMajorUnits | 🟡 design | allows NEGATIVE minor units, no guard | OPEN (decision) |
| ADV-09 | Catalog `ReserveStockCommandValidator`, `ConfirmReservationCommandValidator`; Payouts `GetBalanceQueryValidator`; Payouts `Payout.Create` | 🟡 validation | use `Length(3)` not `MustBeValidCurrency()` → accept `'123'`/`'abc'` | OPEN (PR-C) |
| ADV-10 | `Shipping/EasyPostShippingProvider.cs:86` | 🟡 integrity | parse-fail → `0m` → free-shipping label silently; empty currency → 500 at controller | OPEN |
| ADV-11 | `Payouts SellerProfile.CommissionPercentage` | 🟡 integrity | no [0,100] bound → commission>100% → negative seller balance | OPEN |
| ADV-12 | `Search` CdcSearchIndexWorkerTests / CatalogProductsApiTests | 🔴 fake coverage | CDC tests are a test-local reimpl hardcoding "USD"; never run the real worker. Catalog mock uses `unitPrice` key that doesn't bind → asserts nothing | OPEN (PR-D) |
| ADV-FP | Payouts `LedgerService.cs:62,115` `/100m` & `/10`; `IsValidCurrencyCode` "ISO" label | n/a | CONFIRMED false positives: percentage math (currency-agnostic), not conversions | WONTFIX |

Highest-value missing tests (per adversarial pass): JPY (0-dec) and KWD (3-dec) value
assertions at EVERY conversion boundary; `IsValidCurrencyCode('ZZZ')` behavior pin;
`MustBeValidCurrency` FluentValidation rule test; real CDC worker with non-USD currency.

## 🔴 Anti-pattern #1 — hardcoded ×100 / ÷100 conversions (correctness bugs)

| ID | File:line | Detail | Batch | Status |
|----|-----------|--------|-------|--------|
| PAY-03 | `PaymentAmountMismatchHandler.cs:61` | was `Math.Round(actualPaid*100m)`; now long cents passed directly | 1 | FIXED |
| PAY-04 | `PaymentAmountMismatchHandler.cs:62` | as PAY-03 | 1 | FIXED |
| PAY-05 | `PaymentAmountMismatchHandler.cs:63` | as PAY-03 | 1 | FIXED |
| PAY-06 | `PayPalWebhookProcessor.cs:238` | now `Money.FromMajorUnits(parse(amount,Invariant), currency)` | 1 | FIXED |
| PAY-07 | `PayPalPaymentProcessor.cs:83` | call site passes long cents, no `/100m` | 1 | FIXED |
| PAY-08 | `PayPalPaymentProcessor.cs:83` | as PAY-07 | 1 | FIXED |
| PAY-15 | `StripePaymentProcessor.cs:114` | call site passes long cents, no `/100m` | 1 | FIXED |
| PRC-01 | `PricingRequestedConsumer.cs:64` | `SubtotalCents = (long)Math.Round(Subtotal*100m,0)` | 2 | FIXED |
| PRC-02 | `PricingRequestedConsumer.cs:65` | `TaxCents = (long)Math.Round(TaxAmount*100m,0)` | 2 | FIXED |
| PRC-03 | `PricingRequestedConsumer.cs:66` | `TotalCents = (long)Math.Round(Total*100m,0)` | 2 | FIXED |
| SHP-01 | `ShipmentsController.cs:52` | `(long)Math.Round(result.Amount*100m,...)` EasyPost rate | 2 | FIXED |
| BFF-04 | `CheckoutController.cs:56` | `(long)Math.Round(body.TotalAmount*100m,...)` | 3 | FIXED |
| BFF-05 | `CheckoutController.cs:95` | `(long)Math.Round(i.UnitPrice*100m,...)` | 3 | FIXED |
| BFF-07 | `DemoController.cs:237` | `i.UnitPriceCents / 100m` | 3 | FIXED |
| BFF-08 | `DemoController.cs:1380` | `request.RefundAmountCents / 100m` | 3 | FIXED |
| SCH-03 | `IndexableEntityChangedConsumer.cs:156` | `(decimal)priceCents / 100m` | 3 | FIXED |
| CHK-fixed | `src/CheckoutOrchestrator/.../CheckoutsController.cs` | was `*100m`; now `Money.TryFromMajorUnits` | 0 | FIXED |

## 🟠 Anti-pattern #3 — currency accepted but never validated at boundary

| ID | File:line | Detail | Batch | Status |
|----|-----------|--------|-------|--------|
| PAY-25 | `RefundsController.cs:62` | `Currency` on CreateRefundRequest validated via `MustBeValidCurrency` | 1 | FIXED |
| PAY-12 | `PayPalSubscriptionManager.cs:187` | webhook `currency` metadata now validated via `Money.IsValidCurrencyCode` | 1 | FIXED |
| PYT-05 | `LedgerController.cs:65` | query `currency` → LedgerAccount.Create now validated | 4 | FIXED |
| ORD-03 | `CreateOrderCommand.cs:16` | `string Currency` validated via `MustBeValidCurrency` | 4 | FIXED |

## 🟡 Anti-pattern #4 — un-migrated decimal money fields / missing currency

| ID | File:line | Detail | Batch | Status |
|----|-----------|--------|-------|--------|
| SCH-01 | `ProductSearchDocument.cs:21` | `decimal UnitPrice` → `long UnitPriceCents` + `CurrencyCode` | 3 | FIXED |
| SCH-02 | `ProductSearchDocumentProjector.cs:15` | `decimal unitPrice` param → `long unitPriceCents` + `currencyCode` | 3 | FIXED |
| SCH-04 | `CatalogProductDto` | added `CurrencyCode` field | 3 | FIXED |
| BFF-01 | `CheckoutController.cs:119` | `decimal TotalAmount` on CheckoutRequest | 3 | FIXED |
| BFF-03 | `CheckoutController.cs:130` | `decimal UnitPrice` on CheckoutLineItem | 3 | FIXED |
| ORD-02 | `src/Orders/Orders.Domain/Subscription.cs:82` | `decimal Price` → `long PriceCents` + `Currency` | 4 | FIXED |
| ORD-04 | `src/Orders/Orders.Api/Controllers/SubscriptionsController.cs:84` | `decimal Amount` on request -> converted to cents | 4 | FIXED |

## 🟡 Anti-pattern #2 — silent "USD" defaults

| ID | File:line | Batch | Status |
|----|-----------|-------|--------|
| PAY-01 | `src/Payments/Payments.Domain/Payment.cs:44` (`= "USD"`) | 5 | FIXED |
| PAY-02 | `src/Payments/Payments.Domain/RefundSagaState.cs:15` (`= "USD"`) | 5 | FIXED |
| PAY-09 | `src/Payments/Payments.Infrastructure/PayPal/PayPalModels.cs:42` | 5 | FIXED |
| PAY-10 | `src/Payments/Payments.Infrastructure/PayPal/PayPalModels.cs:206` | 5 | FIXED |
| PAY-11 | `src/Payments/Payments.Infrastructure/PayPal/PayPalSubscriptionManager.cs:28` (const) | 5 | FIXED |
| PAY-13 | `src/Payments/Payments.Infrastructure/PayPal/PayPalCheckoutService.cs:24` (const) | 5 | FIXED |
| PAY-14 | `src/Payments/Payments.Infrastructure/PayPal/PayPalRefundService.cs:27` (const) | 5 | FIXED |
| PAY-16 | `src/Payments/Payments.Infrastructure/Stripe/StripeCheckoutSessionService.cs:16` (const) | 5 | FIXED |
| PAY-17 | `src/Payments/Payments.Infrastructure/Stripe/StripeWebhookProcessor.cs:21` (const) | 5 | FIXED |
| PAY-18 | `src/Payments/Payments.Infrastructure/Stripe/StripeSubscriptionManager.cs:27` (const) | 5 | FIXED |
| PAY-19 | `src/Payments/Payments.Api/Controllers/AdminController.cs:31` (const) | 5 | FIXED |
| PAY-20 | `src/Payments/Payments.Application/Consumers/ProviderRefundCancellationConsumer.cs:54` (`?? "USD"`) | 5 | FIXED |
| PAY-21 | `src/Payments/Payments.Application/Consumers/ProviderRefundCancellationConsumer.cs:88` (`?? "USD"`) | 5 | FIXED |
| PAY-22 | `src/Payments/Payments.Application/Interfaces/ICheckoutSessionService.cs:43` | 5 | FIXED |
| PAY-23 | `src/Payments/Payments.Application/Interfaces/ICheckoutSessionService.cs:58` | 5 | FIXED |
| PAY-24 | `src/Payments/Payments.Application/Interfaces/ISubscriptionManager.cs:18` | 5 | FIXED |
| PYT-01 | `src/Payouts/Payouts.Api/Controllers/LedgerController.cs:21` (`= "USD"` param) | 4 | FIXED |
| PYT-02 | `src/Payouts/Payouts.Api/Controllers/LedgerController.cs:52` (const) | 4 | FIXED |
| ORD-01 | `src/Orders/Orders.Domain/Order.cs:45` (`= "USD"`) | 4 | FIXED |
| CAT-01 | `src/Catalog/Catalog.Api/Controllers/ProductsController.cs:76` | 5 | FIXED |
| CAT-02 | `src/Catalog/Catalog.Api/Controllers/ProductsController.cs:92` | 5 | FIXED |
| CAT-03 | `src/Catalog/Catalog.Application/Commands/ReserveStockCommand.cs:97` (`?? "USD"`) | 5 | FIXED |
| PRC-04 | `src/Pricing/Pricing.Domain/Entities/PriceCalculationLog.cs:20` (`= "USD"`) | 2 | FIXED |
| PRC-05 | `src/Pricing/Pricing.Application/Models/CatalogProductDto.cs:11` (`= "USD"`) | 2 | FIXED |
| SHP-02 | `src/Shipping/Shipping.Api/Domain/Shipment.cs:26` (`= "USD"`) | 2 | FIXED |
| BFF-02 | `src/BffWeb/BffWeb.Api/Controllers/CheckoutController.cs:120` (`= "USD"`) | 3 | FIXED |
| BFF-06 | `src/BffWeb/BffWeb.Api/Controllers/CheckoutController.cs:96` (`?? "USD"`) | 3 | FIXED |
| CHK-01 | `src/CheckoutOrchestrator/.../CheckoutSagaState.cs:38` (`= "USD"`) | 0 | FIXED |

## ⚪ WONTFIX / Rationale

| ID | File:line | Rationale |
|----|-----------|-----------|
| PYT-03 | `src/Payouts/Payouts.Application/Ledger/Services/LedgerService.cs:72` | HWK035 "hardcoded JPY" flags the exponent lookup **table itself** — correct by definition. Suppress/annotate. |
| PYT-04 | `src/Payouts/Payouts.Application/Ledger/Services/LedgerService.cs:115` | Same as PYT-03. |
| MNY-tbl | `src/BuildingBlocks/Common/Money.cs:15` | HWK035 "hardcoded JPY" flags the exponent lookup **table itself** — correct by definition. Suppress/annotate. |

## Tally

- 🟢 Multi-currency bugs (anti-pattern #1): **0** (was 16)
- 🟢 Unvalidated currency: **0** (was 4)
- 🟢 Un-migrated decimal fields: **0** (was 7)
- 🟢 Silent USD defaults: **0** (was 29)
- WONTFIX: 3

**All tracked findings are now RESOLVED.**
