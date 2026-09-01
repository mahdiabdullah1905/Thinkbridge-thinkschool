import http from "k6/http";
import { check } from "k6";

// Identical load shape to day - 11/task - 1/load-test.js (same VUs, duration, executor, both
// endpoints) so the two runs are a fair before/after comparison. Only the scenario name changed
// (authors_slow -> authors_fixed) to reflect that /api/authors now runs the single grouped query
// from Task 2's fix instead of the N+1 loop. GET /api/quotes is unchanged, still the baseline.
export const options = {
  summaryTrendStats: ["avg", "min", "med", "p(90)", "p(95)", "p(99)", "max"],
  scenarios: {
    authors_fixed: {
      executor: "constant-vus",
      exec: "authorsFixed",
      vus: 10,
      duration: "30s",
    },
    quotes_baseline: {
      executor: "constant-vus",
      exec: "quotesBaseline",
      vus: 10,
      duration: "30s",
    },
  },
  thresholds: {
    "http_req_duration{scenario:authors_fixed}": [],
    "http_req_duration{scenario:quotes_baseline}": [],
  },
};

const BASE_URL = __ENV.BASE_URL || "http://localhost:5299";

export function authorsFixed() {
  const res = http.get(`${BASE_URL}/api/authors`);
  check(res, { "authors: status is 200": (r) => r.status === 200 });
}

export function quotesBaseline() {
  const res = http.get(`${BASE_URL}/api/quotes?page=1&size=10`);
  check(res, { "quotes: status is 200": (r) => r.status === 200 });
}
