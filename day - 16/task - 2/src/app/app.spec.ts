import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { App } from './app';
import { authInterceptor } from './interceptors/auth-interceptor';
import { errorMappingInterceptor } from './interceptors/error-mapping-interceptor';
import { retryInterceptor } from './interceptors/retry-interceptor';
import { PaginatedResponse, QuoteListItem } from './quotes-api';

const LIST_RESPONSE: PaginatedResponse<QuoteListItem> = {
  page: 1,
  size: 5,
  totalCount: 1,
  items: [{ id: 1, author: 'Author A', textPreview: 'Preview A', authorQuoteCount: 1 }],
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

  it('shows a loading state, then renders the real list shape once the store loads it', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.autoDetectChanges();
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Loading quotes');

    httpMock.expectOne((req) => req.url === '/api/quotes').flush(LIST_RESPONSE);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Preview A');
  });

  it('clicking a quote loads and renders its detail from the store', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.autoDetectChanges();
    await fixture.whenStable();

    httpMock.expectOne((req) => req.url === '/api/quotes').flush(LIST_RESPONSE);
    await fixture.whenStable();

    fixture.nativeElement.querySelector('.quote-button').click();
    await fixture.whenStable();

    httpMock.expectOne('/api/quotes/1').flush({ id: 1, author: 'Author A', text: 'Full text A', isDeleted: false });
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Full text A');
  });
});
