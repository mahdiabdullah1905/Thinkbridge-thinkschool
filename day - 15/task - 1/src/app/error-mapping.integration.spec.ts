import { HttpClient, provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { AppError } from './errors/app-error';
import { authInterceptor } from './interceptors/auth-interceptor';
import { errorMappingInterceptor } from './interceptors/error-mapping-interceptor';
import { retryInterceptor } from './interceptors/retry-interceptor';

// End-to-end: the FULL interceptor chain (auth -> error-mapping -> retry),
// no HttpTestingController, against the real running Week-1 API
// (day - 2/QuotesApi, http://localhost:5225). This is the actual claim the
// task asks to verify - that a real ProblemDetails/ValidationProblemDetails
// response reaches calling code as a friendly AppError, not a raw
// HttpErrorResponse with a JSON blob for a message.
const API_BASE = 'http://localhost:5225';

describe('error-mapping through the full interceptor chain (real API, no mocks)', () => {
  let http: HttpClient;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(
          withFetch(),
          withInterceptors([authInterceptor, errorMappingInterceptor, retryInterceptor]),
        ),
      ],
    });
    http = TestBed.inject(HttpClient);
  });

  it('turns a real 400 ValidationProblemDetails from POST /api/auth/login into a friendly "validation" AppError', async () => {
    try {
      await firstValueFrom(
        http.post(`${API_BASE}/api/auth/login`, { email: 'not-an-email', password: '' }),
      );
      throw new Error('expected the request to fail');
    } catch (err) {
      const appError = err as AppError;
      expect(appError.kind).toBe('validation');
      expect(appError.status).toBe(400);
      // Real server message, not a made-up one - and definitely not raw JSON.
      expect(appError.message).toBe('The Email field is not a valid e-mail address.');
      if (appError.kind === 'validation') {
        expect(appError.fieldErrors['Email']).toEqual([
          'The Email field is not a valid e-mail address.',
        ]);
      }
    }
  });

  it('turns a real bare 404 from GET /api/quotes/{id} into a friendly "unknown" AppError', async () => {
    try {
      await firstValueFrom(http.get(`${API_BASE}/api/quotes/999999`));
      throw new Error('expected the request to fail');
    } catch (err) {
      const appError = err as AppError;
      expect(appError.kind).toBe('unknown');
      expect(appError.status).toBe(404);
      expect(appError.message).toBe('That could not be found.');
    }
  });

  it('still returns real, correctly-shaped data on success through the same chain', async () => {
    const response = await firstValueFrom(
      http.get<{ page: number; items: unknown[] }>(`${API_BASE}/api/quotes`, {
        params: { page: 1, size: 1 },
      }),
    );
    expect(response.page).toBe(1);
    expect(response.items.length).toBeLessThanOrEqual(1);
  });
});
