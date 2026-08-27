import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { timer } from 'rxjs';
import { retry } from 'rxjs/operators';

const MAX_RETRY_ATTEMPTS = 2;
const BASE_DELAY_MS = 200;

/** Retry only failures that might succeed on their own: no response reached
 *  the server (status 0) or the server said it's transiently broken (5xx).
 *  A 4xx means the request itself was wrong - retrying it just repeats the
 *  same rejection, so those are never retried. */
function isTransientFailure(err: unknown): boolean {
  return err instanceof HttpErrorResponse && (err.status === 0 || err.status >= 500);
}

// Functional interceptors run in the array order given to withInterceptors()
// for the outgoing request, and in reverse for the response - so this needs
// to sit closer to the backend than the error-mapping interceptor, or it
// would be retrying already-mapped AppErrors instead of raw HttpErrorResponses.
export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET') {
    return next(req);
  }

  return next(req).pipe(
    retry({
      count: MAX_RETRY_ATTEMPTS,
      delay: (error, retryAttempt) => {
        if (!isTransientFailure(error)) {
          throw error;
        }
        // Exponential backoff: 200ms, then 400ms.
        return timer(BASE_DELAY_MS * 2 ** (retryAttempt - 1));
      },
    }),
  );
};
