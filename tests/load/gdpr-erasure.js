import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';
import { getAuthHeaders } from './helpers/auth.js';
import { BASE_URL, SLO, lightStages, thinkTime, pseudoUuid, idempotencyKey } from './helpers/config.js';

// ── Custom metrics ──────────────────────────────────────────────────
const erasureLatency     = new Trend('gdpr_erasure_latency', true);
const createLatency      = new Trend('gdpr_create_latency', true);
const erasureSuccess     = new Rate('gdpr_erasure_success_rate');
const erasureCount       = new Counter('gdpr_erasure_total');
const erasureTimeouts    = new Counter('gdpr_erasure_timeouts');

// ── Options ─────────────────────────────────────────────────────────
export const options = {
  scenarios: {
    gdpr_erasure: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: 3 },
        { duration: '2m', target: 10 },
        { duration: '3m', target: 10 },
        { duration: '1m', target: 20 },
        { duration: '2m', target: 20 },
        { duration: '30s', target: 0 },
      ],
    },
  },
  thresholds: {
    'gdpr_erasure_latency':      [`p(95)<${SLO.gdpr_p95}`],
    'gdpr_create_latency':       ['p(95)<2000'],
    'gdpr_erasure_success_rate': ['rate>0.95'],
    'http_req_failed':           ['rate<0.05'],
  },
};

const MAX_POLL_ATTEMPTS = 60;
const POLL_INTERVAL_SEC = 2;

// ── Default function ────────────────────────────────────────────────
export default function () {
  const { headers } = getAuthHeaders(BASE_URL);
  const e2eStart = Date.now();
  let completed = false;

  // ── Step 1: Initiate erasure request ──────────────────────────
  let requestId = null;

  group('Create Erasure Request', () => {
    const reqHeaders = Object.assign({}, headers, {
      'X-Idempotency-Key': idempotencyKey('gdpr'),
    });

    // Each VU erases a unique synthetic user
    const subjectId = __ENV.SUBJECT_USER_ID || pseudoUuid();

    const payload = JSON.stringify({
      subjectId,
      requestType: 'erasure',
      reason: 'user_request',
      requesterEmail: `gdpr-test-${__VU}@loadtest.haworks.dev`,
      scope: ['profile', 'orders', 'payments', 'media', 'notifications', 'audit'],
      metadata: {
        source: 'k6-load-test',
        vuId: __VU,
      },
    });

    const res = http.post(
      `${BASE_URL}/api/v1/privacy/requests`,
      payload,
      { headers: reqHeaders, tags: { name: 'gdpr_create' } }
    );

    createLatency.add(res.timings.duration);

    const created = check(res, {
      'erasure request created 2xx': (r) => r.status >= 200 && r.status < 300,
    });

    if (created) {
      try {
        const body = JSON.parse(res.body);
        requestId = body.id || body.requestId;
      } catch (_) {
        // parse failure
      }
    }
  });

  // ── Step 2: Poll until all services complete erasure ──────────
  if (requestId) {
    group('Poll Erasure Completion', () => {
      for (let attempt = 0; attempt < MAX_POLL_ATTEMPTS; attempt++) {
        sleep(POLL_INTERVAL_SEC);

        const res = http.get(
          `${BASE_URL}/api/v1/privacy/requests/${requestId}`,
          { headers, tags: { name: 'gdpr_poll' } }
        );

        if (res.status === 200) {
          try {
            const body = JSON.parse(res.body);
            const status = (body.status || '').toLowerCase();

            if (status === 'completed' || status === 'done') {
              completed = true;
              break;
            }

            if (status === 'partially_completed' || status === 'partial') {
              // Cross-service fan-out — some services done, others pending
              // Log progress if available
              const progress = body.serviceResults || body.progress || {};
              const total = Object.keys(progress).length;
              const done = Object.values(progress).filter(
                (s) => (s.status || s || '').toString().toLowerCase() === 'completed'
              ).length;

              if (total > 0 && done === total) {
                completed = true;
                break;
              }
            }

            if (status === 'failed' || status === 'error') {
              break;
            }
          } catch (_) {
            // continue polling
          }
        }

        if (attempt === MAX_POLL_ATTEMPTS - 1) {
          erasureTimeouts.add(1);
        }
      }
    });
  }

  erasureLatency.add(Date.now() - e2eStart);
  erasureSuccess.add(completed);
  erasureCount.add(1);

  sleep(thinkTime(2, 5));
}
