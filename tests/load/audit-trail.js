import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';
import { getAuthHeaders } from './helpers/auth.js';
import { BASE_URL, SLO, standardStages, thinkTime } from './helpers/config.js';

// ── Custom metrics ──────────────────────────────────────────────────
const queryLatency       = new Trend('audit_query_latency', true);
const querySuccess       = new Rate('audit_query_success_rate');
const resultCountMetric  = new Trend('audit_result_count', false);
const emptyResults       = new Counter('audit_empty_results');
const auditQueries       = new Counter('audit_queries_total');

// ── Options ─────────────────────────────────────────────────────────
export const options = {
  scenarios: {
    audit_queries: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: standardStages,
    },
  },
  thresholds: {
    'audit_query_latency':      [`p(95)<${SLO.audit_p95}`],
    'audit_query_success_rate': [`rate>${SLO.success_rate}`],
    'http_req_failed':          [`rate<${SLO.error_rate}`],
  },
};

// ── Query patterns ──────────────────────────────────────────────────
const EVENT_TYPES = [
  'order.created', 'order.completed', 'payment.processed',
  'refund.initiated', 'user.login', 'merchant.onboarded',
  'media.uploaded', 'notification.sent',
];

function buildTimeRange(minutesBack) {
  const now = new Date();
  const from = new Date(now.getTime() - minutesBack * 60 * 1000);
  return {
    from: from.toISOString(),
    to: now.toISOString(),
  };
}

// ── Default function ────────────────────────────────────────────────
export default function () {
  const { headers } = getAuthHeaders(BASE_URL);
  const roll = Math.random();

  if (roll < 0.50) {
    // 50% — query recent audit entries (last 30 minutes)
    group('Recent Audit Query', () => {
      const { from, to } = buildTimeRange(30);
      const res = http.get(
        `${BASE_URL}/api/v1/audit?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`,
        { headers, tags: { name: 'audit_recent' } }
      );

      queryLatency.add(res.timings.duration);
      auditQueries.add(1);

      const ok = check(res, {
        'audit query 200': (r) => r.status === 200,
      });
      querySuccess.add(ok);

      if (ok) {
        try {
          const body = JSON.parse(res.body);
          const items = body.items || body.entries || body;
          const count = Array.isArray(items) ? items.length : (body.totalCount || body.total || 0);
          resultCountMetric.add(count);

          check(res, {
            'has audit entries': () => count > 0,
          });

          if (count === 0) {
            emptyResults.add(1);
          }
        } catch (_) {
          // parse failure
        }
      }
    });
  } else if (roll < 0.80) {
    // 30% — filtered by event type
    group('Filtered Audit Query', () => {
      const eventType = EVENT_TYPES[Math.floor(Math.random() * EVENT_TYPES.length)];
      const { from, to } = buildTimeRange(60);

      const res = http.get(
        `${BASE_URL}/api/v1/audit?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}&eventType=${encodeURIComponent(eventType)}`,
        { headers, tags: { name: 'audit_filtered' } }
      );

      queryLatency.add(res.timings.duration);
      auditQueries.add(1);

      const ok = check(res, {
        'filtered query 200': (r) => r.status === 200,
      });
      querySuccess.add(ok);

      if (ok) {
        try {
          const body = JSON.parse(res.body);
          const items = body.items || body.entries || body;
          const count = Array.isArray(items) ? items.length : (body.totalCount || body.total || 0);
          resultCountMetric.add(count);
          if (count === 0) emptyResults.add(1);
        } catch (_) {
          // parse failure
        }
      }
    });
  } else {
    // 20% — wide time range query (last 24 hours)
    group('Wide Audit Query', () => {
      const { from, to } = buildTimeRange(1440);

      const res = http.get(
        `${BASE_URL}/api/v1/audit?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}&take=50`,
        { headers, tags: { name: 'audit_wide' } }
      );

      queryLatency.add(res.timings.duration);
      auditQueries.add(1);

      const ok = check(res, {
        'wide query 200': (r) => r.status === 200,
      });
      querySuccess.add(ok);

      if (ok) {
        try {
          const body = JSON.parse(res.body);
          const items = body.items || body.entries || body;
          const count = Array.isArray(items) ? items.length : (body.totalCount || body.total || 0);
          resultCountMetric.add(count);
          if (count === 0) emptyResults.add(1);
        } catch (_) {
          // parse failure
        }
      }
    });
  }

  sleep(thinkTime(0.5, 2));
}
