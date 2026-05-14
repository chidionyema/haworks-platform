# Parallel Agent Execution Plan

> 7 agents, each owns a non-overlapping set of services/files.
> No agent touches another agent's files. Zero merge conflicts.

---

## Agent 1: PAYMENTS (all waves)
**Scope**: `src/Payments/`, `tests/Payments/`
**Issues**: C-01 thru C-08 from original Payments audit + Payments wave items
**Tasks**:
1. `Payment.cs`: Add guards to `MarkCompleted` (reject double-complete, reject non-Processing/Pending), `MarkRefunded` (reject non-Completed)
2. `CreateRefundCommand.cs`: Validate payment is Completed, validate amount <= payment.Amount
3. `CancelSubscriptionCommand.cs`: Add `IPaymentDbContext` dep, verify `subscription.UserId == request.UserId` before cancel
4. `IPaymentDbContext.cs`: Add `DbSet<Subscription> Subscriptions`
5. `PaymentTests.cs`: Add 14 state transition edge case tests (double-complete, refund wrong status, etc.)
6. `RefundEdgeCaseTests.cs`: Add 11 integration tests (over-refund, zero, negative, wrong status)
7. `SubscriptionTests.cs`: Add 6 domain tests (expiry, cancel, validation)

**Files touched** (exclusive to this agent):
- `src/Payments/**/*`
- `tests/Payments/**/*`

---

## Agent 2: ORDERS + CHECKOUT
**Scope**: `src/Orders/`, `tests/Orders/`, `src/CheckoutOrchestrator/`, `tests/CheckoutOrchestrator/`
**Tasks**:
1. `OrdersController.cs`: Add ownership check to `Get(Guid id)` — verify userId matches or isAdmin
2. `RefundCompletedConsumer.cs`: Change `GetByIdAsync` to `GetByIdTrackedAsync`
3. `RefundCancelledConsumer.cs`: Change `GetByIdAsync` to `GetByIdTrackedAsync`
4. `CreateOrderCommandValidator.cs`: Change `GreaterThanOrEqualTo(0)` to `GreaterThan(0)`
5. `CheckoutSaga.cs:229-233`: Add `StockReleaseRequestedEvent` publish before `TransitionTo(RequiresReview)` on PaymentAmountMismatch
6. `StartCheckoutCommandValidator.cs`: Change amount to `GreaterThan(0)`, add sum-of-items check
7. Add migration for unique index on `CheckoutSagaState.OrderId`
8. Tests: Add `GET_order_403_non_owner`, `RefundCompleted_transitions_order`, `RefundCancelled_reverts_order`, `zero_amount_rejected`, `PaymentAmountMismatch_releases_stock`, `duplicate_OrderId_rejected`

**Files touched**:
- `src/Orders/**/*`
- `tests/Orders/**/*`
- `src/CheckoutOrchestrator/**/*`
- `tests/CheckoutOrchestrator/**/*`

---

## Agent 3: CATALOG
**Scope**: `src/Catalog/`, `tests/Catalog/`
**Tasks**:
1. `ProductsController.cs`: Add `[Authorize]` to class level (all mutations require auth)
2. `CategoriesController.cs`: Add `[Authorize(Roles = "Admin")]` to Create
3. `ConfirmReservationCommand.cs`: Add `reservation.UserId != request.UserId` check
4. `Product.cs`: Add `string CreatedByUserId` property, set in `Create()`, enforce in Update/Delete
5. Add EF migration for `created_by_user_id` column
6. Tests: `POST_product_401_unauthenticated`, `ConfirmReservation_403_non_owner`, `UpdateProduct_403_non_creator`, `DELETE_product_403_non_creator`

**Files touched**:
- `src/Catalog/**/*`
- `tests/Catalog/**/*`

---

## Agent 4: IDENTITY
**Scope**: `src/Identity/`, `tests/Identity/`
**Tasks**:
1. `AuthenticationController.cs`: Evaluate CSRF — if API-only (bearer tokens), document ADR. If browser-facing, remove `[IgnoreAntiforgeryToken]`
2. `DependencyInjection.cs:65`: Change `RequireNonAlphanumeric = false` to `true`
3. `JwtTokenService.cs:85-123`: Add revocation check to sync `ValidateToken`
4. `ExternalAuthenticationController.cs:162-174`: Validate relative redirect URLs (must start with `/`, no `..`)
5. `ExternalAuthenticationController.cs:50-68`: Add `[EnableRateLimiting("auth")]`
6. Tests: `Register_without_special_char_rejected`, `ValidateToken_revoked_returns_false`, `Challenge_path_traversal_redirect_rejected`

**Files touched**:
- `src/Identity/**/*`
- `tests/Identity/**/*`

---

## Agent 5: BFFWEB + CONTENT
**Scope**: `src/BffWeb/`, `tests/BffWeb/`, `src/Content/`, `tests/Content/`
**Tasks**:
1. `CheckoutController.cs`: Add `[Authorize]`, replace `body.UserId` with `HttpContext.GetForwardedUserId()`
2. `DemoController.cs`: Add `[Authorize(Roles = "Admin")]` to `relay-pause`, `relay-resume`, `vault/rotate`
3. `LocationsController.cs` (BffWeb): Add `[Authorize]`
4. `SearchController.cs` (BffWeb): Add `[Authorize]` to SaveSearch, add payload size validation
5. `CheckoutHub.cs`: Add `[Authorize]`, add ownership check on `SubscribeToSaga`
6. `ContentController.cs`: Add `OwnerUserId` to `DeleteContentCommand`, add ownership check in handler
7. `FileSignatureValidator.cs`: Add MIME type allowlist, reject unknown types
8. Tests: `POST_checkout_401_unauthenticated`, `POST_checkout_uses_jwt_userId`, `Delete_content_403_non_owner`, `Upload_exe_rejected`

**Files touched**:
- `src/BffWeb/**/*`
- `tests/BffWeb/**/*`
- `src/Content/**/*`
- `tests/Content/**/*`

---

## Agent 6: LOCATION + SCHEDULER + NOTIFICATIONS
**Scope**: `src/Location/`, `tests/Location/`, `src/Scheduler/`, `tests/Scheduler/`, `src/Notifications/`, `tests/Notifications/`
**Tasks**:
1. `AddressesController.cs` (Location): Add `[Authorize]`
2. `Address.cs`: Add `string UserId` property
3. `GetNearby`: Add max radius (50km), add lat/lon bounds validation
4. `CreateAddressCommand.cs`: Require both lat+lon or neither
5. `Program.cs` (Scheduler): Add `app.UseAuthentication();` before `UseAuthorization()`
6. `SchedulingController.cs`: Add `[Authorize]`
7. `ScheduleEventCommand.cs:17`: Change to `.Must(t => t > DateTimeOffset.UtcNow)`
8. `Program.cs` (Scheduler): Add `DashboardOptions` with auth filter to `UseHangfireDashboard()`
9. `RateLimitBucket.cs`: Implement `Create()` method (remove `NotImplementedException`)
10. `SendNotificationCommandValidator.cs`: Add email format + phone E.164 validation
11. Tests: `POST_addresses_401`, `GetNearby_rejects_huge_radius`, `Schedule_past_time_rejected`, `Hangfire_dashboard_401`, `Send_email_invalid_rejected`, `RateLimit_blocks_after_cap`

**Files touched**:
- `src/Location/**/*`
- `tests/Location/**/*`
- `src/Scheduler/**/*`
- `tests/Scheduler/**/*`
- `src/Notifications/**/*`
- `tests/Notifications/**/*`

---

## Agent 7: PAYOUTS + PRIVACY + WEBHOOKS
**Scope**: `src/Payouts/`, `tests/Payouts/`, `src/Privacy/`, `tests/Privacy/`, `src/Webhooks/`, `tests/Webhooks/`
**Tasks**:
1. `Program.cs` (Payouts): Add `app.UseAuthentication();`
2. `PaymentCompletedConsumer.cs`: Replace `Guid.NewGuid()` with `@event.SellerId` (add to contract if missing)
3. `PayoutsController.cs`, `LedgerController.cs`, `SellersController.cs`: Add ownership checks
4. `LedgerAccount.cs`: Add negative balance guard
5. `Payout.cs`: Add positive amount guard
6. `DisbursementService.cs`: Wrap in transaction, add concurrency token
7. `Program.cs` (Privacy): Add `app.UseAuthentication();`
8. `PrivacyRequestsController.cs`: Extract UserId from JWT, not body
9. `PrivacyRequestStateMachine.cs`: Add PaymentsCompleted to completion check, add 30-day timeout
10. `SubscriptionHandlers.cs` (Webhooks): Add PartnerId filter to all queries
11. `SubscriptionsController.cs` (Webhooks): Extract PartnerId from JWT
12. `SubscriptionValidators.cs` (Webhooks): Add SSRF protection (https-only, block private IPs)
13. `WebhooksDbContext.cs`: Add unique index on `(SubscriptionId, EventId)` for idempotency
14. Tests: `PaymentCompleted_credits_correct_seller`, `negative_balance_rejected`, `erasure_403_wrong_user`, `subscription_403_non_owner`, `SSRF_private_ip_rejected`

**Files touched**:
- `src/Payouts/**/*`
- `tests/Payouts/**/*`
- `src/Privacy/**/*`
- `tests/Privacy/**/*`
- `src/Webhooks/**/*`
- `tests/Webhooks/**/*`

---

## Shared Files (touched by multiple agents — COORDINATE)

These files may need changes from multiple agents. Assign to ONE agent and have others reference:

| File | Assigned To | Reason |
|------|-------------|--------|
| `src/Contracts/Payments/PaymentCompletedEvent.cs` | Agent 7 | Add SellerId field |
| `src/BuildingBlocks/Common/Error.cs` | Agent 1 | May need new error types |
| `CLAUDE.md` | None (updated separately) | Coding guidelines |
