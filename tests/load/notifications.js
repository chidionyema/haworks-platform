import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';
import { getAuthHeaders } from './helpers/auth.js';
import { BASE_URL, SLO, lightStages, thinkTime, pseudoUuid, idempotencyKey } from './helpers/config.js';

// ── Custom metrics ──────────────────────────────────────────────────
const deliveryLatency    = new Trend('notification_delivery_latency', true);
const createLatency      = new Trend('notification_create_latency', true);
const deliverySuccess    = new Rate('notification_delivery_rate');
const notificationCount  = new Counter('notification_total');
const deliveryTimeouts   = new Counter('notification_poll_timeouts');

// ── Options ─────────────────────────────────────────────────────────
export const options = {
  scenarios: {
    notifications: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: lightStages,
    },
  },
  thresholds: {
    'notification_delivery_latency': [`p(95)<${SLO.notification_p95}`],
    'notification_create_latency':   ['p(95)<1000'],
    'notification_delivery_rate':    [`rate>${SLO.success_rate}`],
    'http_req_failed':               [`rate<${SLO.error_rate}`],
  },
};

const NOTIFICATION_TYPES = ['order_confirmation', 'shipping_update', 'refund_processed', 'welcome'];
const MAX_POLL_ATTEMPTS = 30;
const POLL_INTERVAL_SEC = 1;

// ── Default function ────────────────────────────────────────────────
export default function () {
  const { headers } = getAuthHeaders(BASE_URL);
  const e2eStart = Date.now();
  let delivered = false;

  // ── Step 1: Create notification ───────────────────────────────
  let notificationId = null;

  group('Create Notification', () => {
    const reqHeaders = Object.assign({}, headers, {
      'X-Idempotency-Key': idempotencyKey('notif'),
    });

    const notifType = NOTIFICATION_TYPES[Math.floor(Math.random() * NOTIFICATION_TYPES.length)];

    const payload = JSON.stringify({
      userId: __ENV.TARGET_USER_ID || pseudoUuid(),
      type: notifType,
      channel: 'email',
      subject: `Load test notification - ${notifType}`,
      body: `This is a load test notification from VU ${__VU}, iteration ${__ITER}.`,
      metadata: {
        source: 'k6-load-test',
        vuId: __VU,
        iteration: __ITER,
      },
    });

    const res = http.post(
      `${BASE_URL}/api/v1/notifications`,
      payload,
      { headers: reqHeaders, tags: { name: 'notification_create' } }
    );

    createLatency.add(res.timings.duration);

    const created = check(res, {
      'notification created 2xx': (r) => r.status >= 200 && r.status < 300,
    });

    if (created) {
      try {
        const body = JSON.parse(res.body);
        notificationId = body.id || body.notificationId;
      } catch (_) {
        // parse failure
      }
    }
  });

  // ── Step 2: Poll until delivered ──────────────────────────────
  if (notificationId) {
    group('Poll Notification Status', () => {
      for (let attempt = 0; attempt < MAX_POLL_ATTEMPTS; attempt++) {
        sleep(POLL_INTERVAL_SEC);

        const res = http.get(
          `${BASE_URL}/api/v1/notifications/${notificationId}`,
          { headers, tags: { name: 'notification_poll' } }
        );

        if (res.status === 200) {
          try {
            const body = JSON.parse(res.body);
            const status = (body.status || '').toLowerCase();

            if (status === 'sent' || status === 'delivered') {
              delivered = true;
              break;
            }

            if (status === 'failed' || status === 'bounced' || status === 'rejected') {
              break;
            }
          } catch (_) {
            // continue polling
          }
        }

        if (attempt === MAX_POLL_ATTEMPTS - 1) {
          deliveryTimeouts.add(1);
        }
      }
    });
  }

  deliveryLatency.add(Date.now() - e2eStart);
  deliverySuccess.add(delivered);
  notificationCount.add(1);

  sleep(thinkTime(1, 3));
}
