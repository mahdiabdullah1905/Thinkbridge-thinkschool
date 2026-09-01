import http from "k6/http";
import { check } from "k6";

// GET /api/authors is the deliberately slow endpoint added for this task: it fetches the
// distinct author names, then loops and issues one more query per author (N+1), against a
// Quotes.Author column that has no index. GET /api/quotes is the existing, already-fast
// paginated endpoint, hit at the same rate as a baseline for comparison.
export const options = {
  summaryTrendStats: ["avg", "min", "med", "p(90)", "p(95)", "p(99)", "max"],
  scenarios: {
    authors_slow: {
      executor: "constant-vus",
      exec: "authorsSlow",
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
    "http_req_duration{scenario:authors_slow}": [],
    "http_req_duration{scenario:quotes_baseline}": [],
  },
};

const BASE_URL = __ENV.BASE_URL || "http://localhost:5299";

export function authorsSlow() {
  const res = http.get(`${BASE_URL}/api/authors`);
  check(res, { "authors: status is 200": (r) => r.status === 200 });
}

export function quotesBaseline() {
  const res = http.get(`${BASE_URL}/api/quotes?page=1&size=10`);
  check(res, { "quotes: status is 200": (r) => r.status === 200 });
}
