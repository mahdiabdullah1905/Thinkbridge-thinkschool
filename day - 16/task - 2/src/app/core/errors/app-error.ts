// Typed error the UI is meant to render, instead of a raw HttpErrorResponse.
// Every branch carries a `message` that is already safe to show to a user.
export type AppError = ValidationAppError | ProblemAppError | NetworkAppError | UnknownAppError;

export interface ValidationAppError {
  readonly kind: 'validation';
  readonly status: number;
  readonly message: string;
  /** Field name -> messages, straight from ASP.NET's ValidationProblemDetails.errors. */
  readonly fieldErrors: Readonly<Record<string, readonly string[]>>;
  readonly traceId?: string;
}

export interface ProblemAppError {
  readonly kind: 'problem';
  readonly status: number;
  readonly message: string;
  readonly title?: string;
  readonly traceId?: string;
}

export interface NetworkAppError {
  readonly kind: 'network';
  readonly status: 0;
  readonly message: string;
}

// A non-2xx response the API sent with no ProblemDetails body at all - the
// real API returns 401/404 this way (confirmed with curl: empty body, no
// Content-Type). Still needs a friendly message, just not a field/title one.
export interface UnknownAppError {
  readonly kind: 'unknown';
  readonly status: number;
  readonly message: string;
}

export function isAppError(value: unknown): value is AppError {
  return (
    typeof value === 'object' &&
    value !== null &&
    'kind' in value &&
    ((value as { kind: unknown }).kind === 'validation' ||
      (value as { kind: unknown }).kind === 'problem' ||
      (value as { kind: unknown }).kind === 'network' ||
      (value as { kind: unknown }).kind === 'unknown')
  );
}
