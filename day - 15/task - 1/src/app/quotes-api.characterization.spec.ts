import { HttpClient, provideHttpClient, withFetch } from '@angular/common/http';
import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

// Characterization test: pins the REAL Week-1 API contract as it actually
// behaves today, before any interceptor touches it. No HttpTestingController
// here - every request in this file goes to the real, running
// `day - 2/QuotesApi` process on http://localhost:5225 (start it with
// `dotnet run` from that directory before running this suite).
//
// Every shape asserted below was confirmed with curl against that same
// running instance - see the task write-up for the exact commands/output.
const API_BASE = 'http://localhost:5225';

describe('QuotesApi characterization (real API, no mocks)', () => {
  let http: HttpClient;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withFetch())],
    });
    http = TestBed.inject(HttpClient);
  });

  it('GET /api/quotes?page=&size= returns {page,size,totalCount,items:[{id,author,textPreview,authorQuoteCount}]}', async () => {
    const response = await firstValueFrom(
      http.get<{
        page: number;
        size: number;
        totalCount: number;
        items: Array<{ id: number; author: string; textPreview: string; authorQuoteCount: number }>;
      }>(`${API_BASE}/api/quotes`, { params: { page: 1, size: 2 } }),
    );

    expect(response.page).toBe(1);
    expect(response.size).toBe(2);
    expect(typeof response.totalCount).toBe('number');
    expect(Array.isArray(response.items)).toBe(true);
    expect(response.items.length).toBeLessThanOrEqual(2);

    for (const item of response.items) {
      expect(typeof item.id).toBe('number');
      expect(typeof item.author).toBe('string');
      expect(typeof item.textPreview).toBe('string');
      expect(typeof item.authorQuoteCount).toBe('number');
    }
  });

  it('POST /api/auth/login with an invalid body returns 400 ValidationProblemDetails (application/problem+json, an "errors" map)', async () => {
    try {
      await firstValueFrom(
        http.post(
          `${API_BASE}/api/auth/login`,
          { email: 'not-an-email', password: '' },
          { observe: 'response' },
        ),
      );
      throw new Error('expected the request to fail with 400');
    } catch (err) {
      expect(err).toBeInstanceOf(HttpErrorResponse);
      const httpErr = err as HttpErrorResponse;
      expect(httpErr.status).toBe(400);
      expect(httpErr.headers.get('content-type')).toContain('application/problem+json');

      const body = httpErr.error as {
        title: string;
        status: number;
        errors: Record<string, string[]>;
        traceId: string;
      };
      expect(body.status).toBe(400);
      expect(body.errors).toBeTruthy();
      expect(body.errors['Email']).toBeTruthy();
      expect(body.errors['Password']).toBeTruthy();
      expect(typeof body.traceId).toBe('string');
    }
  });

  it('GET /api/quotes/{id} for a non-existent id returns a bare 404 with no body (not ProblemDetails)', async () => {
    try {
      await firstValueFrom(http.get(`${API_BASE}/api/quotes/999999`, { observe: 'response' }));
      throw new Error('expected the request to fail with 404');
    } catch (err) {
      expect(err).toBeInstanceOf(HttpErrorResponse);
      const httpErr = err as HttpErrorResponse;
      expect(httpErr.status).toBe(404);
      // The real API returns Results.NotFound() with no body at all - confirmed
      // with curl: empty response, no Content-Type header. Assuming every 4xx
      // is a ProblemDetails would be wrong for this endpoint.
      expect(httpErr.error == null || httpErr.error === '').toBe(true);
    }
  });

  it('POST /api/quotes without a token returns a bare 401 with no body', async () => {
    try {
      await firstValueFrom(
        http.post(`${API_BASE}/api/quotes`, { author: 'X', text: 'Y' }, { observe: 'response' }),
      );
      throw new Error('expected the request to fail with 401');
    } catch (err) {
      expect(err).toBeInstanceOf(HttpErrorResponse);
      const httpErr = err as HttpErrorResponse;
      expect(httpErr.status).toBe(401);
      expect(httpErr.error == null || httpErr.error === '').toBe(true);
    }
  });
});
