import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';
import crypto from 'k6/crypto';
import { BASE_URL, SLO, lightStages, thinkTime, pseudoUuid } from './helpers/config.js';

// ── Custom metrics ──────────────────────────────────────────────────
const webhookLatency     = new Trend('webhook_latency', true);
const webhookSuccess     = new Rate('webhook_success_rate');
const idempotencyRate    = new Rate('webhook_idempotency_rate');
const webhookCount       = new Counter('webhook_total');
const duplicateCount     = new Counter('webhook_duplicates_sent');

// ── Options ─────────────────────────────────────────────────────────
export const options = {
  scenarios: {
    stripe_webhooks: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: lightStages,
    },
  },
  thresholds: {
    'webhook_latency':         [`p(95)<${SLO.webhook_p95}`],
    'webhook_success_rate':    ['rate>0.99'],
    'webhook_idempotency_rate': ['rate>0.99'],
    'http_req_failed':         [`rate<${SLO.error_rate}`],
  },
};

// ── Stripe HMAC signing ─────────────────────────────────────────────
const WEBHOOK_SECRET = __ENV.STRIPE_WEBHOOK_SECRET || 'whsec_test_load_secret';

function signPayload(payload, timestamp) {
  const signedContent = `${timestamp}.${payload}`;
  const signature = crypto.hmac('sha256', WEBHOOK_SECRET, signedContent, 'hex');
  return `t=${timestamp},v1=${signature}`;
}

function buildStripeEvent(eventId, sessionId, amountCents) {
  return JSON.stringify({
    id: eventId,
    type: 'checkout.session.completed',
    created: Math.floor(Date.now() / 1000),
    data: {
      object: {
        id: sessionId,
        object: 'checkout.session',
        payment_status: 'paid',
        amount_total: amountCents,
        currency: 'usd',
        customer: `cus_loadtest_${__VU}`,
        metadata: {
          orderId: pseudoUuid(),
          sagaId: pseudoUuid(),
        },
      },
    },
    livemode: false,
    api_version: '2023-10-16',
  });
}

// ── Default function ────────────────────────────────────────────────
export default function () {
  const eventId = `evt_loadtest_${__VU}_${__ITER}_${Date.now()}`;
  const sessionId = `cs_loadtest_${__VU}_${__ITER}`;
  const payload = buildStripeEvent(eventId, sessionId, 4999);
  const timestamp = Math.floor(Date.now() / 1000);
  const signature = signPayload(payload, timestamp);

  // ── Phase 1: Send initial webhook ─────────────────────────────
  let firstOk = false;
  group('Initial Webhook', () => {
    const res = http.post(
      `${BASE_URL}/api/v1/payments/webhooks/stripe`,
      payload,
      {
        headers: {
          'Content-Type': 'application/json',
          'Stripe-Signature': signature,
        },
        tags: { name: 'webhook_initial' },
      }
    );

    firstOk = check(res, {
      'webhook 200': (r) => r.status === 200,
    });

    webhookLatency.add(res.timings.duration);
    webhookSuccess.add(firstOk);
    webhookCount.add(1);
  });

  // ── Phase 2: Idempotency test — send same event 2 more times ──
  group('Idempotency Retries', () => {
    let idempotent = true;

    for (let retry = 0; retry < 2; retry++) {
      duplicateCount.add(1);
      const res = http.post(
        `${BASE_URL}/api/v1/payments/webhooks/stripe`,
        payload,
        {
          headers: {
            'Content-Type': 'application/json',
            'Stripe-Signature': signature,
          },
          tags: { name: 'webhook_retry' },
        }
      );

      // Idempotent processing should return 200 (accepted but not reprocessed)
      // or 409 (conflict / already processed) — both are valid
      const ok = check(res, {
        'idempotent response': (r) => r.status === 200 || r.status === 409 || r.status === 204,
      });

      if (!ok) {
        idempotent = false;
      }

      sleep(0.2); // small gap between retries
    }

    idempotencyRate.add(idempotent);
  });

  sleep(thinkTime(1, 3));
}
