// Shared configuration for all k6 load tests
// Usage: import { BASE_URL, SLO, commonOptions, jsonHeaders } from './helpers/config.js';

export const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';

// SLO thresholds (milliseconds unless noted)
export const SLO = {
  browse_p95: 200,
  search_p95: 500,
  checkout_p99: 30000,
  webhook_p95: 1000,
  refund_p95: 5000,
  notification_p95: 10000,
  ai_ttft_p95: 2000,
  ai_total_p95: 10000,
  merchant_p95: 500,
  ledger_p95: 500,
  gdpr_p95: 30000,
  audit_p95: 1000,
  error_rate: 0.01,        // < 1%
  success_rate: 0.99,      // > 99%
  success_rate_high: 0.999, // > 99.9%
};

// Standard ramping-vus stages
export const standardStages = [
  { duration: '1m', target: 10 },   // warm up
  { duration: '3m', target: 50 },   // ramp to steady state
  { duration: '5m', target: 50 },   // hold steady
  { duration: '2m', target: 100 },  // push to peak
  { duration: '3m', target: 100 },  // hold peak
  { duration: '1m', target: 0 },    // drain
];

// Lighter stages for workloads that hit external services
export const lightStages = [
  { duration: '30s', target: 5 },
  { duration: '2m', target: 20 },
  { duration: '3m', target: 20 },
  { duration: '1m', target: 40 },
  { duration: '2m', target: 40 },
  { duration: '30s', target: 0 },
];

// JSON content-type headers
export function jsonHeaders(token) {
  return {
    'Content-Type': 'application/json',
    Authorization: `Bearer ${token}`,
  };
}

// Random think time between min and max seconds
export function thinkTime(minSec, maxSec) {
  return minSec + Math.random() * (maxSec - minSec);
}

// Generate a deterministic idempotency key
export function idempotencyKey(prefix) {
  return `k6-${prefix}-${__VU}-${__ITER}-${Date.now()}`;
}

// UUID v4-ish for test data (not cryptographic)
export function pseudoUuid() {
  const s = () => Math.floor(Math.random() * 0x10000).toString(16).padStart(4, '0');
  return `${s()}${s()}-${s()}-4${s().slice(1)}-${s()}-${s()}${s()}${s()}`;
}
