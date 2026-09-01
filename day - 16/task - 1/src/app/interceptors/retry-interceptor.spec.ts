import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { retryInterceptor } from './retry-interceptor';

describe('retryInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([retryInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('retries a GET that fails with a 503, then succeeds on the next attempt', async () => {
    const result$ = http.get('/api/quotes');
    let resolved: unknown;
    result$.subscribe((value) => (resolved = value));

    // 1st attempt fails with a transient 5xx.
    httpMock.expectOne('/api/quotes').flush('server unavailable', {
      status: 503,
      statusText: 'Service Unavailable',
    });

    // Give the backoff timer a tick to fire the retry.
    await new Promise((resolve) => setTimeout(resolve, 300));

    // 2nd attempt (the retry) succeeds.
    const retryReq = httpMock.expectOne('/api/quotes');
    retryReq.flush({ page: 1, size: 5, totalCount: 0, items: [] });

    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(resolved).toEqual({ page: 1, size: 5, totalCount: 0, items: [] });
  });

  it('retries a GET up to the configured limit, then surfaces the final failure', async () => {
    const result$ = http.get('/api/quotes');
    let failure: unknown;
    result$.subscribe({ error: (err) => (failure = err) });

    // Attempt 1 (original) + 2 retries = 3 total requests, all failing.
    for (let attempt = 0; attempt < 3; attempt++) {
      httpMock.expectOne('/api/quotes').flush('down', { status: 503, statusText: 'Service Unavailable' });
      await new Promise((resolve) => setTimeout(resolve, 500));
    }

    expect(failure).toBeTruthy();
    expect((failure as { status: number }).status).toBe(503);
  });

  it('does NOT retry a GET that fails with a 400 (client error, not transient)', async () => {
    const result$ = http.get('/api/quotes');
    let failure: unknown;
    result$.subscribe({ error: (err) => (failure = err) });

    httpMock.expectOne('/api/quotes').flush('bad request', { status: 400, statusText: 'Bad Request' });
    await new Promise((resolve) => setTimeout(resolve, 300));

    // No second request should have been made - expectNone throws if there is one.
    httpMock.expectNone('/api/quotes');
    expect((failure as { status: number }).status).toBe(400);
  });

  it('does NOT retry a POST, even on a transient 503', async () => {
    const result$ = http.post('/api/quotes', { author: 'A', text: 'B' });
    let failure: unknown;
    result$.subscribe({ error: (err) => (failure = err) });

    httpMock.expectOne('/api/quotes').flush('down', { status: 503, statusText: 'Service Unavailable' });
    await new Promise((resolve) => setTimeout(resolve, 300));

    httpMock.expectNone('/api/quotes');
    expect((failure as { status: number }).status).toBe(503);
  });
});
