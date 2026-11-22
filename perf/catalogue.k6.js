// Load test for a deployed GameVault. Run it against the live URL, never against someone else's site:
//
//   k6 run -e BASE_URL=https://your-gamevault.example perf/catalogue.k6.js
//
// The spec's target is a 95th-percentile response time under 300 ms for cached catalogue pages. The first request for each
// distinct URL after a deploy (or after the 5-minute cache lifetime) is a cache miss, so the script warms every URL once
// before the measured stage and reports misses separately from the steady state.
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend } from 'k6/metrics';

const BASE = (__ENV.BASE_URL || 'http://localhost:5099').replace(/\/$/, '');

// Pages a visitor actually browses. Replace the slugs with ones that exist on the target.
const PAGES = [
  '/',
  '/games',
  '/games?sort=TopRated',
  '/games?genre=puzzle',
  '/games?platform=android&sort=Released',
  '/games?page=2',
  '/games/hades',
  '/games/celeste',
  '/games/untangle',
  '/games/untangle/suggestions',
];

const warm = new Trend('warm_page_duration', true);

export const options = {
  scenarios: {
    warmup: { executor: 'shared-iterations', vus: 1, iterations: PAGES.length, maxDuration: '1m', exec: 'warmup' },
    browse: {
      executor: 'ramping-vus', startVUs: 1, startTime: '15s', exec: 'browse',
      stages: [{ duration: '30s', target: 20 }, { duration: '1m', target: 20 }, { duration: '15s', target: 0 }],
    },
  },
  thresholds: {
    // The headline number: cached pages under 300 ms at the 95th percentile.
    'warm_page_duration': ['p(95)<300'],
    'http_req_failed{scenario:browse}': ['rate<0.01'],
    'checks': ['rate>0.99'],
  },
};

export function warmup() {
  http.get(BASE + PAGES[__ITER % PAGES.length]);
}

export function browse() {
  const path = PAGES[Math.floor(Math.random() * PAGES.length)];
  const res = http.get(BASE + path, { headers: { 'Accept-Encoding': 'gzip, br' } });
  check(res, {
    'status is 200': (r) => r.status === 200,
    'has the security policy': (r) => !!r.headers['Content-Security-Policy'],
  });
  warm.add(res.timings.duration);
  sleep(Math.random() * 1.5 + 0.5); // a visitor reads the page before clicking on
}
