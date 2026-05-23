import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';
import { getAuthHeaders } from './helpers/auth.js';
import { BASE_URL, SLO, standardStages, thinkTime, pseudoUuid, idempotencyKey } from './helpers/config.js';

// ── Custom metrics ──────────────────────────────────────────────────
const creditLatency      = new Trend('ledger_credit_latency', true);
const debitLatency       = new Trend('ledger_debit_latency', true);
const balanceLatency     = new Trend('ledger_balance_latency', true);
const ledgerSuccess      = new Rate('ledger_operation_success_rate');
const balanceDrift       = new Counter('ledger_balance_drift');
const ledgerOps          = new Counter('ledger_total_operations');

// ── Options ─────────────────────────────────────────────────────────
export const options = {
  scenarios: {
    ledger_ops: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: standardStages,
    },
  },
  thresholds: {
    'ledger_credit_latency':          [`p(95)<${SLO.ledger_p95}`],
    'ledger_debit_latency':           [`p(95)<${SLO.ledger_p95}`],
    'ledger_balance_latency':         [`p(95)<${SLO.ledger_p95}`],
    'ledger_operation_success_rate':  [`rate>${SLO.success_rate}`],
    'ledger_balance_drift':           ['count==0'],
    'http_req_failed':                [`rate<${SLO.error_rate}`],
  },
};

// ── Per-VU seller to avoid cross-VU interference in balance checks ─
function getSellerIdForVU() {
  // Each VU gets a deterministic seller ID so balance checks are isolated
  return __ENV.SELLER_ID || `00000000-0000-0000-0000-${String(__VU).padStart(12, '0')}`;
}

// ── Default function ────────────────────────────────────────────────
export default function () {
  const { headers } = getAuthHeaders(BASE_URL);
  const sellerId = getSellerIdForVU();

  // Track expected balance changes within this iteration
  let creditAmount = 0;
  let debitAmount = 0;

  // ── Step 1: Record starting balance ───────────────────────────
  let startBalance = null;

  group('Get Starting Balance', () => {
    const res = http.get(
      `${BASE_URL}/api/v1/payouts/ledger/balance/${sellerId}`,
      { headers, tags: { name: 'ledger_balance' } }
    );

    balanceLatency.add(res.timings.duration);

    if (res.status === 200) {
      try {
        const body = JSON.parse(res.body);
        startBalance = body.balanceCents || body.balance || body.availableCents || 0;
      } catch (_) {
        startBalance = 0;
      }
    } else {
      // New seller — balance is 0
      startBalance = 0;
    }
  });

  // ── Step 2: Credit operations ─────────────────────────────────
  const creditCount = 1 + Math.floor(Math.random() * 3); // 1-3 credits

  group('Credit Operations', () => {
    for (let i = 0; i < creditCount; i++) {
      const amountCents = 1000 + Math.floor(Math.random() * 9000); // $10-$100
      creditAmount += amountCents;

      const payload = JSON.stringify({
        sellerId,
        amountCents,
        currency: 'usd',
        referenceId: pseudoUuid(),
        description: `Load test credit VU${__VU} iter${__ITER} op${i}`,
      });

      const reqHeaders = Object.assign({}, headers, {
        'X-Idempotency-Key': idempotencyKey(`credit-${i}`),
      });

      const res = http.post(
        `${BASE_URL}/api/v1/payouts/ledger/credit`,
        payload,
        { headers: reqHeaders, tags: { name: 'ledger_credit' } }
      );

      creditLatency.add(res.timings.duration);

      const ok = check(res, {
        'credit 2xx': (r) => r.status >= 200 && r.status < 300,
      });

      ledgerSuccess.add(ok);
      ledgerOps.add(1);

      if (!ok) {
        // If credit failed, don't count it in expected balance
        creditAmount -= amountCents;
      }

      sleep(0.1);
    }
  });

  // ── Step 3: Debit operation (smaller than credits to avoid negative) ─
  group('Debit Operation', () => {
    const maxDebit = Math.min(creditAmount, 5000);
    if (maxDebit <= 0) return;

    const amountCents = 100 + Math.floor(Math.random() * (maxDebit - 100));
    debitAmount = amountCents;

    const payload = JSON.stringify({
      sellerId,
      amountCents,
      currency: 'usd',
      referenceId: pseudoUuid(),
      description: `Load test debit VU${__VU} iter${__ITER}`,
    });

    const reqHeaders = Object.assign({}, headers, {
      'X-Idempotency-Key': idempotencyKey('debit'),
    });

    const res = http.post(
      `${BASE_URL}/api/v1/payouts/ledger/debit`,
      payload,
      { headers: reqHeaders, tags: { name: 'ledger_debit' } }
    );

    debitLatency.add(res.timings.duration);

    const ok = check(res, {
      'debit 2xx': (r) => r.status >= 200 && r.status < 300,
    });

    ledgerSuccess.add(ok);
    ledgerOps.add(1);

    if (!ok) {
      debitAmount = 0;
    }
  });

  // ── Step 4: Verify balance consistency ────────────────────────
  group('Verify Balance', () => {
    sleep(0.5); // allow async processing to settle

    const res = http.get(
      `${BASE_URL}/api/v1/payouts/ledger/balance/${sellerId}`,
      { headers, tags: { name: 'ledger_balance_verify' } }
    );

    balanceLatency.add(res.timings.duration);

    if (res.status === 200 && startBalance !== null) {
      try {
        const body = JSON.parse(res.body);
        const endBalance = body.balanceCents || body.balance || body.availableCents || 0;
        const expectedBalance = startBalance + creditAmount - debitAmount;

        const consistent = check(res, {
          'balance consistent': () => endBalance === expectedBalance,
        });

        if (!consistent) {
          // Balance drift detected — this is a critical finding
          balanceDrift.add(1);
          console.warn(
            `BALANCE DRIFT: seller=${sellerId} expected=${expectedBalance} actual=${endBalance} ` +
            `start=${startBalance} credits=${creditAmount} debits=${debitAmount}`
          );
        }
      } catch (_) {
        // parse failure
      }
    }
  });

  sleep(thinkTime(1, 3));
}
