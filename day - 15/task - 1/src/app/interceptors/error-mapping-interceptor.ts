import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { AppError } from '../errors/app-error';

// Raw shapes as actually returned by the API (all three confirmed with curl
// against the running Week-1 API, day - 2/QuotesApi):
//
//   ValidationProblemDetails (e.g. POST /api/auth/login with a bad email):
//     {"type":"...","title":"One or more validation errors occurred.",
//      "status":400,"errors":{"Email":["The Email field is not a valid e-mail address."]},
//      "traceId":"..."}
//     Content-Type: application/problem+json
//
//   Plain ProblemDetails, manually constructed by a handler (e.g. re-adding
//   a quote already in a collection - see ProgramExtensions.MapCollectionEndpoints):
//     {"type":"...","title":"Cannot add quote","status":400,"detail":"Quote 1 is already in the collection."}
//     Content-Type: application/json; charset=utf-8  <-- NOT problem+json.
//     Content-Type cannot be used to tell the two apart; only the presence
//     of an "errors" object can.
//
//   No body at all (e.g. 401 from an unauthenticated request, 404 from
//   GET /api/quotes/{id} for a missing id): empty response body, no
//   Content-Type header. Results.Unauthorized()/Results.NotFound() don't
//   go through ProblemDetails despite AddProblemDetails() being registered.
interface RawValidationProblemDetails {
  readonly title?: string;
  readonly status?: number;
  readonly errors: Record<string, readonly string[]>;
  readonly traceId?: string;
}

interface RawProblemDetails {
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  readonly traceId?: string;
}

function hasFieldErrors(body: unknown): body is RawValidationProblemDetails {
  return (
    typeof body === 'object' &&
    body !== null &&
    'errors' in body &&
    typeof (body as { errors: unknown }).errors === 'object' &&
    (body as { errors: unknown }).errors !== null
  );
}

function looksLikeProblemDetails(body: unknown): body is RawProblemDetails {
  return (
    typeof body === 'object' &&
    body !== null &&
    ('title' in body || 'detail' in body || 'status' in body)
  );
}

function friendlyValidationMessage(problem: RawValidationProblemDetails): string {
  const firstField = Object.keys(problem.errors)[0];
  const firstMessage = firstField ? problem.errors[firstField][0] : undefined;
  return firstMessage ?? problem.title ?? 'Some of the submitted values were invalid.';
}

export function mapHttpErrorToAppError(err: HttpErrorResponse): AppError {
  if (err.status === 0) {
    return {
      kind: 'network',
      status: 0,
      message: 'Could not reach the server. Check your connection and try again.',
    };
  }

  const body: unknown = err.error;

  if (hasFieldErrors(body)) {
    return {
      kind: 'validation',
      status: err.status,
      message: friendlyValidationMessage(body),
      fieldErrors: body.errors,
      traceId: body.traceId,
    };
  }

  if (looksLikeProblemDetails(body)) {
    return {
      kind: 'problem',
      status: err.status,
      message: body.detail ?? body.title ?? 'The request could not be completed.',
      title: body.title,
      traceId: body.traceId,
    };
  }

  return {
    kind: 'unknown',
    status: err.status,
    message: genericMessageForStatus(err.status),
  };
}

function genericMessageForStatus(status: number): string {
  switch (status) {
    case 401:
      return 'You need to sign in to do that.';
    case 403:
      return "You don't have permission to do that.";
    case 404:
      return 'That could not be found.';
    default:
      return status >= 500
        ? 'Something went wrong on the server. Please try again.'
        : 'The request could not be completed.';
  }
}

// Terminal interceptor: converts every HttpErrorResponse into an AppError so
// nothing downstream (components) has to parse ProblemDetails/ValidationProblemDetails
// bodies itself. Must sit closer to the caller than the retry interceptor,
// so retries see the raw HttpErrorResponse and only the final, post-retry
// failure gets mapped.
export const errorMappingInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse) {
        return throwError(() => mapHttpErrorToAppError(err));
      }
      return throwError(() => err);
    }),
  );
