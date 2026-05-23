import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';
import { getAuthHeaders } from './helpers/auth.js';
import { BASE_URL, SLO, standardStages, thinkTime, idempotencyKey } from './helpers/config.js';

// ── Custom metrics ──────────────────────────────────────────────────
const createLatency       = new Trend('merchant_create_latency', true);
const verifyLatency       = new Trend('merchant_verify_latency', true);
const onboardingSuccess   = new Rate('merchant_onboarding_success_rate');
const duplicateSlugErrors = new Counter('merchant_duplicate_slug_errors');
const merchantCount       = new Counter('merchant_total');

// ── Options ─────────────────────────────────────────────────────────
export const options = {
  scenarios: {
    merchant_onboarding: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: standardStages,
    },
  },
  thresholds: {
    'merchant_create_latency':          [`p(95)<${SLO.merchant_p95}`],
    'merchant_verify_latency':          [`p(95)<${SLO.merchant_p95}`],
    'merchant_onboarding_success_rate': [`rate>${SLO.success_rate}`],
    'merchant_duplicate_slug_errors':   ['count==0'],
    'http_req_failed':                  [`rate<${SLO.error_rate}`],
  },
};

// ── Business categories ─────────────────────────────────────────────
const CATEGORIES = [
  'electronics', 'fashion', 'home_garden', 'sports',
  'books', 'food_beverage', 'health_beauty', 'toys',
];

function generateMerchantName() {
  const adjectives = ['Swift', 'Prime', 'Golden', 'Blue', 'Nova', 'Peak', 'True', 'Core'];
  const nouns = ['Market', 'Store', 'Shop', 'Hub', 'Depot', 'Trade', 'Supply', 'Goods'];
  const adj = adjectives[Math.floor(Math.random() * adjectives.length)];
  const noun = nouns[Math.floor(Math.random() * nouns.length)];
  // Include VU + iteration + timestamp to ensure uniqueness under concurrency
  return `${adj}${noun}_${__VU}_${__ITER}_${Date.now()}`;
}

// ── Default function ────────────────────────────────────────────────
export default function () {
  const { headers } = getAuthHeaders(BASE_URL);
  let success = false;
  let merchantId = null;

  const merchantName = generateMerchantName();
  const category = CATEGORIES[Math.floor(Math.random() * CATEGORIES.length)];

  // ── Step 1: Create merchant profile ───────────────────────────
  group('Create Merchant', () => {
    const reqHeaders = Object.assign({}, headers, {
      'X-Idempotency-Key': idempotencyKey('merchant'),
    });

    const payload = JSON.stringify({
      name: merchantName,
      slug: merchantName.toLowerCase().replace(/[^a-z0-9]+/g, '-'),
      email: `${merchantName.toLowerCase()}@loadtest.haworks.dev`,
      category,
      description: `Load test merchant created by VU ${__VU}`,
      contactPhone: '+1555000' + String(__VU).padStart(4, '0'),
      address: {
        line1: '123 Load Test Street',
        city: 'Testville',
        state: 'TX',
        postalCode: '78701',
        country: 'US',
      },
    });

    const res = http.post(
      `${BASE_URL}/api/v1/merchants`,
      payload,
      { headers: reqHeaders, tags: { name: 'merchant_create' } }
    );

    createLatency.add(res.timings.duration);

    const created = check(res, {
      'merchant created 2xx': (r) => r.status >= 200 && r.status < 300,
    });

    if (res.status === 409 || res.status === 422) {
      // Duplicate slug detected under concurrency
      try {
        const body = JSON.parse(res.body);
        const msg = (body.message || body.detail || '').toLowerCase();
        if (msg.includes('slug') || msg.includes('duplicate') || msg.includes('already exists')) {
          duplicateSlugErrors.add(1);
        }
      } catch (_) {
        // ignore parse failure
      }
    }

    if (created) {
      try {
        const body = JSON.parse(res.body);
        merchantId = body.id || body.merchantId;
      } catch (_) {
        // parse failure
      }
    }
  });

  // ── Step 2: Verify merchant was created ───────────────────────
  if (merchantId) {
    group('Verify Merchant', () => {
      sleep(0.5); // brief settle time

      const res = http.get(
        `${BASE_URL}/api/v1/merchants/${merchantId}`,
        { headers, tags: { name: 'merchant_verify' } }
      );

      verifyLatency.add(res.timings.duration);

      success = check(res, {
        'merchant exists 200': (r) => r.status === 200,
        'merchant name matches': (r) => {
          if (r.status !== 200) return false;
          try {
            const body = JSON.parse(r.body);
            return (body.name || '').includes(merchantName.split('_')[0]);
          } catch (_) { return false; }
        },
      });
    });
  }

  onboardingSuccess.add(success);
  merchantCount.add(1);

  sleep(thinkTime(1, 3));
}
