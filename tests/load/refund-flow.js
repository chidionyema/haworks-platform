import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';
import { getAuthHeaders } from './helpers/auth.js';
import { BASE_URL, SLO, lightStages, thinkTime, pseudoUuid, idempotencyKey } from './helpers/config.js';

// ── Custom metrics ──────────────────────────────────────────────────
const refundLatency     = new Trend('refund_e2e_latency', true);
const refundCreateTime  = new Trend('refund_create_latency', true);
const refundSuccess     = new Rate('refund_success_rate');
const refundCount       = new Counter('refund_total');
const refundTimeouts    = new Counter('refund_poll_timeouts');

// ── Options ─────────────────────────────────────────────────────────
export const options = {
  scenarios: {
    refund_flow: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: lightStages,
    },
  },
  thresholds: {
    'refund_e2e_latency':    [`p(95)<${SLO.refund_p95}`],
    'refund_create_latency': ['p(95)<1000'],
    'refund_success_rate':   [`rate>${SLO.success_rate}`],
    'http_req_failed':       [`rate<${SLO.error_rate}`],
  },
};

const MAX_POLL_ATTEMPTS = 20;
const POLL_INTERVAL_SEC = 1;

// ── Default function ────────────────────────────────────────────────
export default function () {
  const { headers } = getAuthHeaders(BASE_URL);
  const e2eStart = Date.now();
  let success = false;

  // ── Step 1: Create refund request ─────────────────────────────
  let refundId = null;

  group('Create Refund', () => {
    const reqHeaders = Object.assign({}, headers, {
      'X-Idempotency-Key': idempotencyKey('refund'),
    });

    const payload = JSON.stringify({
      orderId: __ENV.ORDER_ID || pseudoUuid(),
      reason: 'customer_request',
      amountCents: 2999,
      currency: 'usd',
    });

    const res = http.post(
      `${BASE_URL}/api/v1/refunds`,
      payload,
      { headers: reqHeaders, tags: { name: 'refund_create' } }
    );

    refundCreateTime.add(res.timings.duration);

    const created = check(res, {
      'refund created 2xx': (r) => r.status >= 200 && r.status < 300,
    });

    if (created) {
      try {
        const body = JSON.parse(res.body);
        refundId = body.id || body.refundId;
      } catch (_) {
        // parse failure
      }
    }
  });

  // ── Step 2: Poll until terminal state ─────────────────────────
  if (refundId) {
    group('Poll Refund Status', () => {
      for (let attempt = 0; attempt < MAX_POLL_ATTEMPTS; attempt++) {
        sleep(POLL_INTERVAL_SEC);

        const res = http.get(
          `${BASE_URL}/api/v1/refunds/${refundId}`,
          { headers, tags: { name: 'refund_poll' } }
        );

        if (res.status === 200) {
          try {
            const body = JSON.parse(res.body);
            const state = (body.status || body.state || '').toLowerCase();

            if (state === 'refunded' || state === 'completed' || state === 'succeeded') {
              success = true;
              break;
            }

            if (state === 'requiresreview' || state === 'requires_review') {
              // Acceptable terminal state
              success = true;
              break;
            }

            if (state === 'failed' || state === 'rejected' || state === 'cancelled') {
              // Terminal failure — not a timeout
              break;
            }
          } catch (_) {
            // continue polling
          }
        }

        if (attempt === MAX_POLL_ATTEMPTS - 1) {
          refundTimeouts.add(1);
        }
      }
    });
  }

  refundLatency.add(Date.now() - e2eStart);
  refundSuccess.add(success);
  refundCount.add(1);

  sleep(thinkTime(1, 3));
}
