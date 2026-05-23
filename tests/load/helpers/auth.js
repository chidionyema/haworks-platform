// Shared auth helper for all k6 load tests
// Usage: import { getAuthHeaders } from './helpers/auth.js';

import http from 'k6/http';

/**
 * Returns { headers, token } suitable for authenticated API calls.
 *
 * Resolution order:
 *   1. __ENV.AUTH_TOKEN (pre-provisioned JWT)
 *   2. POST /api/v1/authentication/service-token (machine-to-machine)
 *   3. POST /api/v1/authentication/login (user credential flow)
 *
 * The result is cached per-VU on first call.
 */
const _cache = {};

export function getAuthHeaders(baseUrl) {
  if (_cache[__VU]) {
    return _cache[__VU];
  }

  let token = __ENV.AUTH_TOKEN || '';

  if (!token) {
    // Try service-token endpoint
    const svcRes = http.post(
      `${baseUrl}/api/v1/authentication/service-token`,
      null,
      {
        headers: {
          'X-Service-Secret': __ENV.SERVICE_SECRET || 'load-test-secret',
        },
        tags: { name: 'auth' },
      }
    );
    if (svcRes.status === 200) {
      try {
        token = JSON.parse(svcRes.body).accessToken;
      } catch (_) {
        // fall through
      }
    }
  }

  if (!token) {
    // Fallback: user login
    const loginRes = http.post(
      `${baseUrl}/api/v1/authentication/login`,
      JSON.stringify({
        email: `loadtest+${__VU}@haworks.dev`,
        password: 'LoadTest123!',
      }),
      {
        headers: { 'Content-Type': 'application/json' },
        tags: { name: 'auth' },
      }
    );
    if (loginRes.status === 200) {
      try {
        token = JSON.parse(loginRes.body).accessToken;
      } catch (_) {
        // fall through
      }
    }
  }

  const result = {
    token,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    },
  };

  _cache[__VU] = result;
  return result;
}
