import sys

errors = """
Integration / Notifications: src/Notifications/Notifications.Infrastructure/Persistence/Preferences/PreferencesRepository.cs#L84
Integration / Notifications: src/Notifications/Notifications.Infrastructure/Persistence/Preferences/PreferencesRepository.cs#L36
Integration / Notifications: src/Notifications/Notifications.Infrastructure/Persistence/Suppression/SuppressionRepository.cs#L33
Integration / Notifications: src/Notifications/Notifications.Application/Commands/SendNotificationCommand.cs#L82
Integration / Webhooks: src/Webhooks/Webhooks.Api/Controllers/SubscriptionsController.cs#L37
Integration / Webhooks: src/Webhooks/Webhooks.Api/Controllers/DeliveriesController.cs#L20
Integration / Webhooks: src/Webhooks/Webhooks.Api/Controllers/SubscriptionsController.cs#L20
Integration / Webhooks: src/Webhooks/Webhooks.Infrastructure/Persistence/WebhooksDbContext.cs#L37
Integration / Webhooks: src/Webhooks/Webhooks.Infrastructure/Persistence/WebhooksDbContext.cs#L36
Integration / Webhooks: src/Webhooks/Webhooks.Infrastructure/Persistence/WebhooksDbContext.cs#L22
Integration / Webhooks: src/Webhooks/Webhooks.Infrastructure/Persistence/WebhooksDbContext.cs#L21
Integration / Webhooks: src/Webhooks/Webhooks.Infrastructure/Messaging/EventFanOutConsumer.cs#L13
Integration / Webhooks: src/Webhooks/Webhooks.Application/Subscriptions/RotateWebhookSubscriptionSecretCommandValidator.cs#L9
Integration / Webhooks: src/Webhooks/Webhooks.Application/Deliveries/GetDeliveriesQueryValidator.cs#L9
Integration / Orders: tests/Orders/Orders.Integration/OrdersWebAppFactory.cs#L71
Integration / Orders: src/Orders/Orders.Api/Controllers/DemoIdempotencyController.cs#L190
Integration / Orders: src/Orders/Orders.Api/Controllers/OrdersController.cs#L78
Integration / Orders: src/Orders/Orders.Infrastructure/OrderDbContext.cs#L55
Integration / Orders: src/Orders/Orders.Infrastructure/OrderDbContext.cs#L156
Integration / Orders: src/Orders/Orders.Application/Validators/CreateOrderCommandValidator.cs#L19
Integration / Orders: src/Orders/Orders.Application/Validators/CreateOrderCommandValidator.cs#L14
Integration / Orders: src/Orders/Orders.Application/Consumers/PrivacyErasureRequestedConsumer.cs#L44
Integration / Orders: src/Orders/Orders.Application/Consumers/RefundCompletedConsumer.cs#L14
Integration / Orders: src/Orders/Orders.Application/Consumers/PaymentCompletedConsumer.cs#L27
Integration / Catalog: src/Catalog/Catalog.Application/Validators/CreateProductCommandValidator.cs#L13
Integration / Catalog: src/Catalog/Catalog.Application/Validators/DeleteProductCommandValidator.cs#L10
Integration / Catalog: src/Catalog/Catalog.Application/Validators/UpdateCategoryCommandValidator.cs#L10
Integration / Catalog: src/Catalog/Catalog.Application/Validators/ApproveProductReviewCommandValidator.cs#L11
Integration / Catalog: src/Catalog/Catalog.Application/Validators/ApproveProductReviewCommandValidator.cs#L10
Integration / Catalog: src/Catalog/Catalog.Application/Consumers/StockReleaseRequestedConsumer.cs#L47
Integration / Catalog: src/Catalog/Catalog.Application/Validators/UpdateProductCommandValidator.cs#L14
Integration / Catalog: src/Catalog/Catalog.Application/Validators/UpdateProductCommandValidator.cs#L10
Integration / Catalog: src/Catalog/Catalog.Application/Validators/UpdateProductReviewCommandValidator.cs#L11
Integration / Catalog: src/Catalog/Catalog.Application/Validators/UpdateProductReviewCommandValidator.cs#L10
Integration / BffWeb: src/BffWeb/BffWeb.Api/Controllers/DemoController.cs#L1429
Integration / BffWeb: src/BffWeb/BffWeb.Api/Controllers/DemoController.cs#L1407
Integration / BffWeb: src/BffWeb/BffWeb.Api/Controllers/DemoController.cs#L1385
Integration / BffWeb: src/BffWeb/BffWeb.Api/Controllers/DemoController.cs#L1370
Integration / BffWeb: src/BffWeb/BffWeb.Api/Controllers/DemoController.cs#L1318
Integration / BffWeb: src/BffWeb/BffWeb.Api/Program.cs#L206
Integration / BffWeb: src/BffWeb/BffWeb.Api/Program.cs#L205
Integration / BffWeb: src/BffWeb/BffWeb.Api/Controllers/DemoController.cs#L380
Integration / BffWeb: src/BffWeb/BffWeb.Api/Controllers/DemoController.cs#L195
Integration / BffWeb: src/BffWeb/BffWeb.Api/SignalR/SagaStepBridgeConsumers.cs#L95
Integration / Payouts: src/Payouts/Payouts.Api/Controllers/LedgerController.cs#L56
Integration / Payouts: src/Payouts/Payouts.Infrastructure/Messaging/Consumers/PaymentCompletedConsumer.cs#L9
Integration / Payouts: src/Payouts/Payouts.Application/Disbursements/Services/DisbursementService.cs#L174
Integration / Payouts: src/Payouts/Payouts.Application/Disbursements/Services/DisbursementService.cs#L85
Integration / Payouts: src/Payouts/Payouts.Application/Ledger/Services/LedgerService.cs#L199
Integration / Payouts: src/Payouts/Payouts.Application/Ledger/Commands/MatureFunds/MatureFundsCommand.cs#L82
Integration / Search: tests/Search/Search.Integration/SmokeTest.cs#L16
Integration / Search: src/Search/Search.Application/Consumers/IndexableEntityChangedConsumer.cs#L165
Integration / Search: tests/Search/Search.Integration/IndexerTests.cs#L148
Integration / Search: tests/Search/Search.Integration/CdcSearchIndexWorkerTests.cs#L217
Integration / Search: tests/Search/Search.Integration/CdcSearchIndexWorkerTests.cs#L246
Integration / Search: tests/Search/Search.Integration/IndexerTests.cs#L84
Integration / Search: tests/Search/Search.Integration/CdcSearchIndexWorkerTests.cs#L194
Integration / Search: tests/Search/Search.Integration/ElasticsearchIndexTests.cs#L48
Integration / Search: tests/Search/Search.Integration/SmokeTest.cs#L16
Integration / Search: src/Search/Search.Application/Consumers/IndexableEntityChangedConsumer.cs#L165
Integration / Audit: src/Audit/Audit.Infrastructure/Export/AuditExportWorker.cs#L131
Integration / Payments: src/Payments/Payments.Infrastructure/PaymentDbContext.cs#L119
Integration / Payments: src/Payments/Payments.Infrastructure/PaymentDbContext.cs#L176
Integration / Payments: src/Payments/Payments.Infrastructure/PaymentDbContext.cs#L168
Integration / Payments: src/Payments/Payments.Infrastructure/Repositories/PaymentRepository.cs#L34
Integration / Payments: src/Payments/Payments.Infrastructure/Stripe/StripePaymentProcessor.cs#L200
Integration / Payments: src/Payments/Payments.Infrastructure/Stripe/StripeWebhookProcessor.cs#L74
Integration / Payments: src/Payments/Payments.Application/Commands/Secrets/RevokeOldStripeKeyJob.cs#L49
Integration / Payments: src/Payments/Payments.Application/Commands/Refunds/CreateRefundCommand.cs#L25
Integration / Payments: src/Payments/Payments.Application/Webhooks/PaymentAmountMismatchHandler.cs#L34
Integration / Payments: src/Payments/Payments.Application/Commands/Secrets/RotateStripeKeyCommand.cs#L35
"""

for line in errors.strip().split('\n'):
    parts = line.split(': ')
    if len(parts) > 1:
        file_info = parts[1]
        file_path, line_num = file_info.split('#L')
        line_num = int(line_num)
        print(f"\n--- {file_path} L{line_num} ---")
        try:
            with open(file_path, 'r') as f:
                lines = f.readlines()
                start = max(0, line_num - 3)
                end = min(len(lines), line_num + 3)
                for i in range(start, end):
                    prefix = ">> " if i + 1 == line_num else "   "
                    print(f"{prefix}{i+1}: {lines[i].rstrip()}")
        except Exception as e:
            print(f"Error reading file: {e}")
