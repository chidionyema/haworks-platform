# Platform Currency-Handling Audit

**Opened:** 2026-05-30 · **Status:** IN PROGRESS · **Owner:** chidionyema

Single source of truth for the platform-wide currency-correctness effort. Every finding,
its severity, the anti-pattern it violates, its status, and the PR that fixes it are tracked
in [`findings.md`](./findings.md). This file explains the standard, the method, and the plan.

## The standard

The canonical monetary type is `Haworks.BuildingBlocks.Common.Money`
(`src/BuildingBlocks/Common/Money.cs`):

```csharp
public readonly record struct Money(long MinorUnits, string CurrencyCode)
```

- `Money.FromMajorUnits(decimal, currency)` / `Money.TryFromMajorUnits(...)` — major → minor,
  using the **per-currency exponent** (`GetExponent`/`GetMultiplier`): JPY/KRW/VND/CLP/ISK = 0
  decimals (×1), KWD/BHD/OMR = 3 (×1000), everything else = 2 (×100).
- `Money.ToMajorUnits()` — minor → major for display.
- `Money.IsValidCurrencyCode(string?)` — the **single source of truth** for ISO 4217 validation
  (exactly three ASCII uppercase letters).
- `CurrencyValidationExtensions.MustBeValidCurrency()` — shared FluentValidation rule built on the above.

"Appropriate" currency handling means **all** of:
1. Convert major↔minor **only via `Money`** — never hardcoded `×100`/`÷100` (wrong for JPY/KWD).
2. **Validate** every currency code crossing an API / webhook / command boundary.
3. **Never silently default** currency (`?? "USD"`, `= "USD"`). Require it explicitly.
4. **Carry a currency** alongside every monetary amount, end to end (persistence + events).
5. Use `MidpointRounding.AwayFromZero` for all monetary rounding.
6. Never do cross-currency arithmetic without first asserting the currency codes match.

## Anti-pattern catalogue

| # | Anti-pattern | Why it's wrong |
|---|---|---|
| 1 | Hardcoded `×100` / `÷100` conversion | Produces wrong amounts for 0- and 3-decimal currencies (JPY, KWD). **Correctness bug.** |
| 2 | Silent `"USD"` default | Masks a missing/invalid currency; wrong currency persisted/charged. |
| 3 | Currency string never validated at boundary | Untrusted input reaches domain/provider; zero-trust violation. |
| 4 | Monetary `decimal` field not in `long` minor-units | Rounding drift, no currency context; un-migrated. |
| 5 | Cross-currency arithmetic without code check | Silently adds unlike currencies. |
| 6 | Wrong rounding mode | Inconsistent half-unit handling across the platform. |

## Method

Three parallel read-only audits (2026-05-30) swept every service and the shared contracts.
Findings are catalogued in [`findings.md`](./findings.md) and mirrored in the session task list.

## Severity & risk note

The `×100` conversion bugs (#1) are **latent** to the degree the platform is de-facto USD today:
they do not lose money on USD, but they corrupt any non-2-decimal currency the moment one is
introduced, and they live on the real Stripe/PayPal provider paths. They are treated as the
highest-priority fixes because they are correctness bugs on the money path.

## Remediation plan (≤3 services per PR, per branch-protection rules)

| Batch | Scope | Severity | Status |
|---|---|---|---|
| 0 | `Money` helper hardening (IsValidCurrencyCode, overflow-safe From/TryFromMajorUnits, shared validator) + CheckoutOrchestrator | 🔴/🟠 | ✅ built, tests green |
| 1 | Payments: provider/webhook `×100`/`÷100` → Money; validate webhook+refund currency | 🔴 | pending |
| 2 | Pricing + Shipping: consumer/controller conversions → Money | 🔴 | pending |
| 3 | BffWeb + Search: conversions → Money; add CurrencyCode to search doc/DTO | 🔴/🟡 | pending |
| 4 | Orders + Payouts: validate currency at boundaries; migrate decimal money fields | 🟠/🟡 | pending |
| 5 | Platform sweep: remove/justify silent `"USD"` defaults (split per 3-service rule) | 🟡 | pending |

## Definition of done

- Every finding in `findings.md` is `FIXED` or explicitly `WONTFIX` with rationale.
- A `dotnet build` + per-service tests pass for each batch before its PR is pushed.
- An architecture guard is added (or HWK086/HWK035 re-armed) to prevent regression.
</content>
