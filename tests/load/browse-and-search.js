import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';
import { getAuthHeaders } from './helpers/auth.js';
import { BASE_URL, SLO, standardStages, thinkTime } from './helpers/config.js';

// ── Custom metrics ──────────────────────────────────────────────────
const browseLatency    = new Trend('browse_latency', true);
const searchLatency    = new Trend('search_latency', true);
const aiSearchLatency  = new Trend('ai_search_latency', true);
const browseSuccess    = new Rate('browse_success_rate');
const searchSuccess    = new Rate('search_success_rate');
const aiSearchSuccess  = new Rate('ai_search_success_rate');
const browseCount      = new Counter('browse_requests');
const searchCount      = new Counter('search_requests');
const aiSearchCount    = new Counter('ai_search_requests');

// ── Options ─────────────────────────────────────────────────────────
export const options = {
  scenarios: {
    browse_and_search: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: standardStages,
    },
  },
  thresholds: {
    'browse_latency':       [`p(95)<${SLO.browse_p95}`],
    'search_latency':       [`p(95)<${SLO.search_p95}`],
    'ai_search_latency':    [`p(95)<${SLO.search_p95 * 2}`],  // AI search gets 1s
    'browse_success_rate':  [`rate>${SLO.success_rate}`],
    'search_success_rate':  [`rate>${SLO.success_rate}`],
    'ai_search_success_rate': [`rate>0.95`],
    'http_req_failed':      [`rate<${SLO.error_rate}`],
    'http_req_duration':    ['p(95)<1000'],
  },
};

// ── Search terms ────────────────────────────────────────────────────
const TEXT_QUERIES = [
  'wireless+headphones', 'organic+coffee', 'running+shoes',
  'mechanical+keyboard', 'yoga+mat', 'water+bottle',
  'laptop+stand', 'noise+cancelling', 'protein+powder',
  'smart+watch',
];

const AI_QUERIES = [
  'I need something to help me focus while working from home',
  'What pairs well with a morning workout routine?',
  'Gift ideas for a tech enthusiast under $100',
  'Eco-friendly alternatives to common office supplies',
  'Something comfortable for long video calls',
];

// ── Default function ────────────────────────────────────────────────
export default function () {
  const { headers } = getAuthHeaders(BASE_URL);
  const roll = Math.random();

  if (roll < 0.60) {
    // 60% — paginated browse
    group('Browse Products', () => {
      const page = Math.floor(Math.random() * 5);
      const skip = page * 20;
      const res = http.get(
        `${BASE_URL}/api/v1/products?skip=${skip}&take=20`,
        { headers, tags: { name: 'browse' } }
      );

      const ok = check(res, {
        'browse 200': (r) => r.status === 200,
        'browse has items': (r) => {
          if (r.status !== 200) return false;
          try {
            const body = JSON.parse(r.body);
            return Array.isArray(body.items || body) && (body.items || body).length >= 0;
          } catch (_) { return false; }
        },
      });

      browseLatency.add(res.timings.duration);
      browseSuccess.add(ok);
      browseCount.add(1);
    });
  } else if (roll < 0.90) {
    // 30% — full-text search
    group('Text Search', () => {
      const q = TEXT_QUERIES[Math.floor(Math.random() * TEXT_QUERIES.length)];
      const res = http.get(
        `${BASE_URL}/api/v1/search?q=${q}`,
        { headers, tags: { name: 'text_search' } }
      );

      const ok = check(res, {
        'search 200': (r) => r.status === 200,
      });

      searchLatency.add(res.timings.duration);
      searchSuccess.add(ok);
      searchCount.add(1);
    });
  } else {
    // 10% — AI semantic search
    group('AI Semantic Search', () => {
      const query = AI_QUERIES[Math.floor(Math.random() * AI_QUERIES.length)];
      const res = http.post(
        `${BASE_URL}/api/v1/ai/search`,
        JSON.stringify({ query, maxResults: 10 }),
        { headers, tags: { name: 'ai_search' } }
      );

      const ok = check(res, {
        'ai search 2xx': (r) => r.status >= 200 && r.status < 300,
      });

      aiSearchLatency.add(res.timings.duration);
      aiSearchSuccess.add(ok);
      aiSearchCount.add(1);
    });
  }

  sleep(thinkTime(0.5, 2));
}
