import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AppError, ValidationAppError } from '../errors/app-error';
import { errorMappingInterceptor } from './error-mapping-interceptor';

describe('errorMappingInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorMappingInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  // Body shape confirmed for real against POST /api/auth/login with a bad email.
  it('maps a real ValidationProblemDetails body to a "validation" AppError', async () => {
    let caught: AppError | undefined;
    http.post('/api/auth/login', { email: 'bad', password: '' }).subscribe({
      error: (err: AppError) => (caught = err),
    });

    httpMock.expectOne('/api/auth/login').flush(
      {
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: {
          Email: ['The Email field is not a valid e-mail address.'],
          Password: ['The Password field is required.'],
        },
        traceId: '00-abc-def-01',
      },
      { status: 400, statusText: 'Bad Request', headers: { 'Content-Type': 'application/problem+json' } },
    );

    expect(caught?.kind).toBe('validation');
    const validationErr = caught as ValidationAppError;
    expect(validationErr.status).toBe(400);
    expect(validationErr.fieldErrors['Email']).toEqual(['The Email field is not a valid e-mail address.']);
    expect(validationErr.message).toBe('The Email field is not a valid e-mail address.');
    expect(validationErr.traceId).toBe('00-abc-def-01');
  });

  // Body shape confirmed for real against POST /api/collections/{id}/quotes when
  // re-adding a quote already in the collection - note this ProblemDetails body
  // has NO "errors" key and its Content-Type is application/json, not
  // application/problem+json, so the interceptor cannot key off Content-Type.
  it('maps a real plain ProblemDetails body (no "errors" key) to a "problem" AppError', async () => {
    let caught: AppError | undefined;
    http.post('/api/collections/1/quotes', { quoteId: 1 }).subscribe({
      error: (err: AppError) => (caught = err),
    });

    httpMock.expectOne('/api/collections/1/quotes').flush(
      {
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
        title: 'Cannot add quote',
        status: 400,
        detail: 'Quote 1 is already in the collection.',
      },
      { status: 400, statusText: 'Bad Request', headers: { 'Content-Type': 'application/json' } },
    );

    expect(caught?.kind).toBe('problem');
    expect(caught?.message).toBe('Quote 1 is already in the collection.');
    if (caught?.kind === 'problem') {
      expect(caught.title).toBe('Cannot add quote');
    }
  });

  // The real API returns a genuinely empty body for 401/404 (Results.Unauthorized()/
  // Results.NotFound()) - confirmed with curl. Must not crash trying to read
  // "errors" or "detail" off a null body, and still produce a friendly message.
  it('maps a bare 401 with an empty body to an "unknown" AppError with a friendly message', async () => {
    let caught: AppError | undefined;
    http.post('/api/quotes', { author: 'A', text: 'B' }).subscribe({
      error: (err: AppError) => (caught = err),
    });

    httpMock.expectOne('/api/quotes').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(caught?.kind).toBe('unknown');
    expect(caught?.status).toBe(401);
    expect(caught?.message).toBe('You need to sign in to do that.');
  });

  it('maps a bare 404 with an empty body to an "unknown" AppError', async () => {
    let caught: AppError | undefined;
    http.get('/api/quotes/999999').subscribe({
      error: (err: AppError) => (caught = err),
    });

    httpMock.expectOne('/api/quotes/999999').flush(null, { status: 404, statusText: 'Not Found' });

    expect(caught?.kind).toBe('unknown');
    expect(caught?.status).toBe(404);
    expect(caught?.message).toBe('That could not be found.');
  });

  it('maps a network failure (status 0) to a "network" AppError', async () => {
    let caught: AppError | undefined;
    http.get('/api/quotes').subscribe({
      error: (err: AppError) => (caught = err),
    });

    httpMock.expectOne('/api/quotes').error(new ProgressEvent('error'));

    expect(caught?.kind).toBe('network');
    expect(caught?.status).toBe(0);
  });
});
