import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { errorMappingInterceptor } from '../../core/interceptors/error-mapping-interceptor';
import { AUTHOR_MAX_LENGTH, QuoteDetail } from '../../core/quotes-api';
import { CreateQuote } from './create-quote';

describe('CreateQuote', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CreateQuote],
      providers: [
        provideHttpClient(withInterceptors([errorMappingInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  // Creating the component transitively creates QuotesStore (via inject()),
  // whose constructor fires an immediate GET /api/quotes for the list this
  // form doesn't render. Flushing it here keeps every test's own requests
  // isolated to just what that test cares about.
  function render() {
    const fixture = TestBed.createComponent(CreateQuote);
    httpMock
      .expectOne((req) => req.url === '/api/quotes' && req.method === 'GET')
      .flush({ page: 1, size: 5, totalCount: 0, items: [] });
    fixture.autoDetectChanges();
    return fixture;
  }

  it('renders an accessible label and no errors for the empty, untouched form', async () => {
    const fixture = render();
    await fixture.whenStable();

    const author = fixture.nativeElement.querySelector('#author');
    const label = fixture.nativeElement.querySelector('label[for="author"]');
    expect(label).not.toBeNull();
    expect(author.getAttribute('aria-invalid')).toBe('false');
    expect(fixture.nativeElement.querySelector('.field-error')).toBeNull();
  });

  it('shows validation errors and moves focus to the first invalid field on submit', async () => {
    const fixture = render();
    await fixture.whenStable();

    const form = fixture.nativeElement.querySelector('form');
    form.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    const author = fixture.nativeElement.querySelector('#author');
    const authorError = fixture.nativeElement.querySelector('#author-error');
    expect(author.getAttribute('aria-invalid')).toBe('true');
    expect(author.getAttribute('aria-describedby')).toBe('author-error');
    expect(authorError.textContent).toContain('Enter');
    expect(document.activeElement).toBe(author);

    httpMock.expectNone((req) => req.url === '/api/quotes' && req.method === 'POST');
  });

  it('rejects an author over the API limit of 100 characters', async () => {
    const fixture = render();
    const component = fixture.componentInstance;
    component['form'].controls.author.setValue('a'.repeat(AUTHOR_MAX_LENGTH + 1));
    component['form'].controls.author.markAsTouched();
    await fixture.whenStable();

    const authorError = fixture.nativeElement.querySelector('#author-error');
    expect(authorError.textContent).toContain('100 characters or fewer');
  });

  it('rejects a whitespace-only value that [Required] alone would not catch', async () => {
    const fixture = render();
    const component = fixture.componentInstance;
    component['form'].controls.text.setValue('   ');
    component['form'].controls.text.markAsTouched();
    await fixture.whenStable();

    expect(component['form'].controls.text.invalid).toBe(true);
    const textError = fixture.nativeElement.querySelector('#text-error');
    expect(textError.textContent).toContain('Enter');
  });

  it('submits a valid quote, shows the success state, and the store refreshes the list', async () => {
    const fixture = render();
    const component = fixture.componentInstance;
    component['form'].setValue({ author: 'Ada Lovelace', text: 'The Analytical Engine weaves algebra.' });
    await fixture.whenStable();

    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    const req = httpMock.expectOne((r) => r.url === '/api/quotes' && r.method === 'POST');
    expect(req.request.body).toEqual({ author: 'Ada Lovelace', text: 'The Analytical Engine weaves algebra.' });

    const created: QuoteDetail = { id: 1, author: 'Ada Lovelace', text: 'The Analytical Engine weaves algebra.', isDeleted: false };
    req.flush(created, { status: 201, statusText: 'Created' });
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('.success-message').textContent).toContain('Quote #1');

    // QuotesStore.createQuote() refreshes the current page on success.
    httpMock.expectOne((r) => r.url === '/api/quotes' && r.method === 'GET').flush({
      page: 1,
      size: 5,
      totalCount: 1,
      items: [{ id: 1, author: 'Ada Lovelace', textPreview: 'The Analytical Engine...', authorQuoteCount: 1 }],
    });
  });

  it('surfaces a 401 from the auth-protected endpoint as a clear message', async () => {
    const fixture = render();
    const component = fixture.componentInstance;
    component['form'].setValue({ author: 'Ada Lovelace', text: 'Valid text' });
    await fixture.whenStable();

    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    const req = httpMock.expectOne((r) => r.url === '/api/quotes' && r.method === 'POST');
    // A real 401 from this endpoint has no body (day - 2/QuotesApi returns
    // Results.Unauthorized(), matching the shape errorMappingInterceptor's
    // 'unknown' branch is for).
    req.flush(null, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('.server-message').textContent).toContain('sign in');
  });

  it('maps a server-side field validation error onto the right control', async () => {
    const fixture = render();
    const component = fixture.componentInstance;
    component['form'].setValue({ author: 'Ada Lovelace', text: 'Valid text' });
    await fixture.whenStable();

    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    const req = httpMock.expectOne((r) => r.url === '/api/quotes' && r.method === 'POST');
    req.flush(
      {
        title: 'One or more validation errors occurred.',
        errors: { Author: ['The field Author must be a string with a minimum length of 1 and a maximum length of 100.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();

    const author = fixture.nativeElement.querySelector('#author');
    expect(author.getAttribute('aria-invalid')).toBe('true');
    expect(document.activeElement).toBe(author);
  });

  it('reports a network failure distinctly from a server rejection', async () => {
    const fixture = render();
    const component = fixture.componentInstance;
    component['form'].setValue({ author: 'Ada Lovelace', text: 'Valid text' });
    await fixture.whenStable();

    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    const req = httpMock.expectOne((r) => r.url === '/api/quotes' && r.method === 'POST');
    req.error(new ProgressEvent('error'));
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('.server-message').textContent).toContain('Could not reach');
  });
});
