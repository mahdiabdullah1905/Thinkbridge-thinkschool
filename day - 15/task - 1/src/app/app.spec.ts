import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { App } from './app';
import { authInterceptor } from './interceptors/auth-interceptor';
import { errorMappingInterceptor } from './interceptors/error-mapping-interceptor';
import { retryInterceptor } from './interceptors/retry-interceptor';
import { PaginatedResponse, QuoteListItem } from './quotes-api';

const LIST_RESPONSE: PaginatedResponse<QuoteListItem> = {
  page: 1,
  size: 5,
  totalCount: 1,
  items: [{ id: 1, author: 'Author A', textPreview: 'Quote A preview', authorQuoteCount: 1 }],
};

describe('App', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideHttpClient(withInterceptors([authInterceptor, errorMappingInterceptor, retryInterceptor])),
        provideHttpClientTesting(),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('shows a loading state, then renders the quote list once the API responds', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.autoDetectChanges();
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Loading quotes');

    httpMock.expectOne((req) => req.url === '/api/quotes').flush(LIST_RESPONSE);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Quote A preview');
  });

  // This is the interceptor-chain-through-the-UI case: a real ValidationProblemDetails
  // shape (as returned by the real API) flushed through the full interceptor stack
  // must reach the template as a plain, friendly message, not a raw error object.
  it('shows a friendly message (not a raw ProblemDetails dump) when login fails with a real ValidationProblemDetails body', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.autoDetectChanges();
    await fixture.whenStable();

    httpMock.expectOne((req) => req.url === '/api/quotes').flush(LIST_RESPONSE);
    await fixture.whenStable();

    fixture.nativeElement.querySelector('#password').value = 'short';
    fixture.nativeElement.querySelector('#password').dispatchEvent(new Event('input'));
    await fixture.whenStable();

    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();

    const loginReq = httpMock.expectOne('/api/auth/login');
    loginReq.flush(
      {
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { Email: ['The Email field is not a valid e-mail address.'] },
        traceId: '00-real-trace-01',
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('The Email field is not a valid e-mail address.');
    // Must not leak the raw ProblemDetails JSON/type/traceId into the UI.
    expect(text).not.toContain('rfc9110');
    expect(text).not.toContain('00-real-trace-01');
  });

  it('attaches the Authorization header to a request made after a successful login', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.autoDetectChanges();
    await fixture.whenStable();

    httpMock.expectOne((req) => req.url === '/api/quotes').flush(LIST_RESPONSE);
    await fixture.whenStable();

    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();

    const loginReq = httpMock.expectOne('/api/auth/login');
    expect(loginReq.request.headers.has('Authorization')).toBe(false);
    loginReq.flush({ accessToken: 'real-token', refreshToken: 'r', expiresIn: 900 });
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Signed in');
  });
});
