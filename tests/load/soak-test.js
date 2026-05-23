import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';
import { getAuthHeaders } from './helpers/auth.js';
import { BASE_URL, SLO, thinkTime, pseudoUuid, idempotencyKey, jsonHeaders } from './helpers/config.js';

// ── Custom metrics — drift detection ────────────────────────────────
const soakP95Early       = new Trend('soak_p95_early', true);
const soakP95Late        = new Trend('soak_p95_late', true);
const soakOverallSuccess = new Rate('soak_overall_success_rate');
const soakErrorCount     = new Counter('soak_total_errors');
const soakRequestCount   = new Counter('soak_total_requests');

// Per-workload metrics
const browseLatency      = new Trend('soak_browse_latency', true);
const searchLatency      = new Trend('soak_search_latency', true);
const checkoutLatency    = new Trend('soak_checkout_latency', true);
const webhookLatency     = new Trend('soak_webhook_latency', true);
const refundLatency      = new Trend('soak_refund_latency', true);
const notificationLat    = new Trend('soak_notification_latency', true);
const merchantLatency    = new Trend('soak_merchant_latency', true);
const ledgerLatency      = new Trend('soak_ledger_latency', true);
const auditLatency       = new Trend('soak_audit_latency', true);

// ── Options: 1-hour soak at constant arrival rate ───────────────────
export const options = {
  scenarios: {
    browse: {
      executor: 'constant-arrival-rate',
      rate: 5,
      timeUnit: '1s',
      duration: '1h',
      preAllocatedVUs: 10,
      maxVUs: 30,
      exec: 'browseWorkload',
    },
    search: {
      executor: 'constant-arrival-rate',
      rate: 5,
      timeUnit: '1s',
      duration: '1h',
      preAllocatedVUs: 10,
      maxVUs: 30,
      exec: 'searchWorkload',
    },
    checkout: {
      executor: 'constant-arrival-rate',
      rate: 5,
      timeUnit: '1s',
      duration: '1h',
      preAllocatedVUs: 15,
      maxVUs: 40,
      exec: 'checkoutWorkload',
    },
    webhooks: {
      executor: 'constant-arrival-rate',
      rate: 5,
      timeUnit: '1s',
      duration: '1h',
      preAllocatedVUs: 10,
      maxVUs: 30,
      exec: 'webhookWorkload',
    },
    refunds: {
      executor: 'constant-arrival-rate',
      rate: 5,
      timeUnit: '1s',
      duration: '1h',
      preAllocatedVUs: 15,
      maxVUs: 40,
      exec: 'refundWorkload',
    },
    notifications: {
      executor: 'constant-arrival-rate',
      rate: 5,
      timeUnit: '1s',
      duration: '1h',
      preAllocatedVUs: 10,
      maxVUs: 30,
      exec: 'notificationWorkload',
    },
    merchants: {
      executor: 'constant-arrival-rate',
      rate: 5,
      timeUnit: '1s',
      duration: '1h',
      preAllocatedVUs: 10,
      maxVUs: 30,
      exec: 'merchantWorkload',
    },
    ledger: {
      executor: 'constant-arrival-rate',
      rate: 5,
      timeUnit: '1s',
      duration: '1h',
      preAllocatedVUs: 10,
      maxVUs: 30,
      exec: 'ledgerWorkload',
    },
    audit: {
      executor: 'constant-arrival-rate',
      rate: 5,
      timeUnit: '1s',
      duration: '1h',
      preAllocatedVUs: 10,
      maxVUs: 30,
      exec: 'auditWorkload',
    },
  },
  thresholds: {
    'soak_overall_success_rate':  ['rate>0.99'],
    'soak_browse_latency':        ['p(95)<500'],
    'soak_search_latency':        ['p(95)<1000'],
    'soak_checkout_latency':      ['p(95)<5000'],
    'soak_webhook_latency':       ['p(95)<2000'],
    'soak_refund_latency':        ['p(95)<5000'],
    'soak_notification_latency':  ['p(95)<5000'],
    'soak_merchant_latency':      ['p(95)<1000'],
    'soak_ledger_latency':        ['p(95)<1000'],
    'soak_audit_latency':         ['p(95)<2000'],
    'http_req_failed':            ['rate<0.02'],
  },
};

// ── Time-based drift tracking ───────────────────────────────────────
// Record early (first 5 min) vs late (last 5 min) latencies
const SOAK_DURATION_MS = 60 * 60 * 1000; // 1 hour
const EARLY_CUTOFF_MS = 5 * 60 * 1000;   // first 5 minutes
const LATE_START_MS = 55 * 60 * 1000;     // last 5 minutes
let soakStartTime = 0;

function trackDrift(durationMs) {
  if (soakStartTime === 0) soakStartTime = Date.now();
  const elapsed = Date.now() - soakStartTime;

  if (elapsed < EARLY_CUTOFF_MS) {
    soakP95Early.add(durationMs);
  } else if (elapsed > LATE_START_MS) {
    soakP95Late.add(durationMs);
  }
}

function recordResult(ok, latencyMs, metric) {
  soakOverallSuccess.add(ok);
  soakRequestCount.add(1);
  if (!ok) soakErrorCount.add(1);
  metric.add(latencyMs);
  trackDrift(latencyMs);
}

// ── Search terms ────────────────────────────────────────────────────
const SEARCH_TERMS = [
  'wireless+headphones', 'organic+coffee', 'running+shoes',
  'mechanical+keyboard', 'yoga+mat',
];

// ── Workload: Browse ────────────────────────────────────────────────
export function browseWorkload() {
  const { headers } = getAuthHeaders(BASE_URL);
  const page = Math.floor(Math.random() * 5);

  group('Soak: Browse', () => {
    const res = http.get(
      `${BASE_URL}/api/v1/products?skip=${page * 20}&take=20`,
      { headers, tags: { name: 'soak_browse' } }
    );
    const ok = check(res, { 'browse 200': (r) => r.status === 200 });
    recordResult(ok, res.timings.duration, browseLatency);
  });
}

// ── Workload: Search ────────────────────────────────────────────────
export function searchWorkload() {
  const { headers } = getAuthHeaders(BASE_URL);
  const q = SEARCH_TERMS[Math.floor(Math.random() * SEARCH_TERMS.length)];

  group('Soak: Search', () => {
    const res = http.get(
      `${BASE_URL}/api/v1/search?q=${q}`,
      { headers, tags: { name: 'soak_search' } }
    );
    const ok = check(res, { 'search 200': (r) => r.status === 200 });
    recordResult(ok, res.timings.duration, searchLatency);
  });
}

// ── Workload: Checkout ──────────────────────────────────────────────
export function checkoutWorkload() {
  const { headers } = getAuthHeaders(BASE_URL);
  const reqHeaders = Object.assign({}, headers, {
    'X-Idempotency-Key': idempotencyKey('soak-checkout'),
  });

  group('Soak: Checkout', () => {
    // Create reservation
    const resReserve = http.post(
      `${BASE_URL}/api/v1/checkout/reservations`,
      JSON.stringify({
        items: [{
          productId: __ENV.PRODUCT_ID || '00000000-0000-0000-0000-000000000001',
          quantity: 1,
        }],
      }),
      { headers: reqHeaders, tags: { name: 'soak_checkout_reserve' } }
    );

    let ok = check(resReserve, {
      'reserve 2xx': (r) => r.status >= 200 && r.status < 300,
    });

    if (ok) {
      let reservationId = null;
      try {
        const body = JSON.parse(resReserve.body);
        reservationId = body.reservationId || body.id;
      } catch (_) {
        // ignore
      }

      if (reservationId) {
        const resCheckout = http.post(
          `${BASE_URL}/api/v1/checkout`,
          JSON.stringify({ reservationId }),
          { headers: reqHeaders, tags: { name: 'soak_checkout_confirm' } }
        );
        ok = check(resCheckout, {
          'checkout 2xx': (r) => r.status >= 200 && r.status < 300,
        });
      }
    }

    recordResult(ok, resReserve.timings.duration, checkoutLatency);
  });
}

// ── Workload: Webhooks ──────────────────────────────────────────────
export function webhookWorkload() {
  const eventId = `evt_soak_${__VU}_${__ITER}_${Date.now()}`;

  group('Soak: Webhook', () => {
    const payload = JSON.stringify({
      id: eventId,
      type: 'checkout.session.completed',
      created: Math.floor(Date.now() / 1000),
      data: {
        object: {
          id: `cs_soak_${__VU}_${__ITER}`,
          object: 'checkout.session',
          payment_status: 'paid',
          amount_total: 1999,
          currency: 'usd',
          metadata: { orderId: pseudoUuid(), sagaId: pseudoUuid() },
        },
      },
      livemode: false,
      api_version: '2023-10-16',
    });

    const res = http.post(
      `${BASE_URL}/api/v1/payments/webhooks/stripe`,
      payload,
      {
        headers: { 'Content-Type': 'application/json' },
        tags: { name: 'soak_webhook' },
      }
    );

    const ok = check(res, {
      'webhook 2xx or 401': (r) => r.status >= 200 && r.status < 500,
    });
    recordResult(ok, res.timings.duration, webhookLatency);
  });
}

// ── Workload: Refunds ───────────────────────────────────────────────
export function refundWorkload() {
  const { headers } = getAuthHeaders(BASE_URL);
  const reqHeaders = Object.assign({}, headers, {
    'X-Idempotency-Key': idempotencyKey('soak-refund'),
  });

  group('Soak: Refund', () => {
    const res = http.post(
      `${BASE_URL}/api/v1/refunds`,
      JSON.stringify({
        orderId: pseudoUuid(),
        reason: 'customer_request',
        amountCents: 1999,
        currency: 'usd',
      }),
      { headers: reqHeaders, tags: { name: 'soak_refund' } }
    );

    const ok = check(res, {
      'refund 2xx': (r) => r.status >= 200 && r.status < 300,
    });
    recordResult(ok, res.timings.duration, refundLatency);
  });
}

// ── Workload: Notifications ─────────────────────────────────────────
export function notificationWorkload() {
  const { headers } = getAuthHeaders(BASE_URL);
  const reqHeaders = Object.assign({}, headers, {
    'X-Idempotency-Key': idempotencyKey('soak-notif'),
  });

  group('Soak: Notification', () => {
    const res = http.post(
      `${BASE_URL}/api/v1/notifications`,
      JSON.stringify({
        userId: pseudoUuid(),
        type: 'order_confirmation',
        channel: 'email',
        subject: 'Soak test notification',
        body: `Soak test from VU ${__VU}`,
      }),
      { headers: reqHeaders, tags: { name: 'soak_notification' } }
    );

    const ok = check(res, {
      'notification 2xx': (r) => r.status >= 200 && r.status < 300,
    });
    recordResult(ok, res.timings.duration, notificationLat);
  });
}

// ── Workload: Merchants ─────────────────────────────────────────────
export function merchantWorkload() {
  const { headers } = getAuthHeaders(BASE_URL);
  const name = `SoakMerchant_${__VU}_${__ITER}_${Date.now()}`;
  const reqHeaders = Object.assign({}, headers, {
    'X-Idempotency-Key': idempotencyKey('soak-merchant'),
  });

  group('Soak: Merchant', () => {
    const res = http.post(
      `${BASE_URL}/api/v1/merchants`,
      JSON.stringify({
        name,
        slug: name.toLowerCase().replace(/[^a-z0-9]+/g, '-'),
        email: `${name.toLowerCase()}@soak.haworks.dev`,
        category: 'electronics',
        description: 'Soak test merchant',
      }),
      { headers: reqHeaders, tags: { name: 'soak_merchant' } }
    );

    const ok = check(res, {
      'merchant 2xx': (r) => r.status >= 200 && r.status < 300,
    });
    recordResult(ok, res.timings.duration, merchantLatency);
  });
}

// ── Workload: Ledger ────────────────────────────────────────────────
export function ledgerWorkload() {
  const { headers } = getAuthHeaders(BASE_URL);
  const sellerId = `00000000-0000-0000-0000-${String(__VU).padStart(12, '0')}`;
  const reqHeaders = Object.assign({}, headers, {
    'X-Idempotency-Key': idempotencyKey('soak-ledger'),
  });

  group('Soak: Ledger Credit', () => {
    const res = http.post(
      `${BASE_URL}/api/v1/payouts/ledger/credit`,
      JSON.stringify({
        sellerId,
        amountCents: 1000 + Math.floor(Math.random() * 5000),
        currency: 'usd',
        referenceId: pseudoUuid(),
        description: 'Soak test credit',
      }),
      { headers: reqHeaders, tags: { name: 'soak_ledger' } }
    );

    const ok = check(res, {
      'ledger 2xx': (r) => r.status >= 200 && r.status < 300,
    });
    recordResult(ok, res.timings.duration, ledgerLatency);
  });
}

// ── Workload: Audit ─────────────────────────────────────────────────
export function auditWorkload() {
  const { headers } = getAuthHeaders(BASE_URL);
  const now = new Date();
  const from = new Date(now.getTime() - 30 * 60 * 1000).toISOString();
  const to = now.toISOString();

  group('Soak: Audit', () => {
    const res = http.get(
      `${BASE_URL}/api/v1/audit?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`,
      { headers, tags: { name: 'soak_audit' } }
    );

    const ok = check(res, {
      'audit 200': (r) => r.status === 200,
    });
    recordResult(ok, res.timings.duration, auditLatency);
  });
}
