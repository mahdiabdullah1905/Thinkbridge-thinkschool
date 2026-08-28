import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { errorMappingInterceptor } from '../../core/interceptors/error-mapping-interceptor';
import { PaginatedResponse, QuoteDetail, QuoteListItem } from '../../core/quotes-api';
import { QuotesStore } from './quotes-store';

// Only errorMappingInterceptor is wired in for these tests: the store's
// typing (err: AppError in its .subscribe({ error }) handlers) depends on
// that interceptor having already run. auth/retry are irrelevant to state
// transitions and are covered by their own interceptor spec files - retry's
// real backoff timers would just make these tests slow for no benefit here.
function setUp() {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(withInterceptors([errorMappingInterceptor])), provideHttpClientTesting()],
  });
  const httpMock = TestBed.inject(HttpTestingController);
  return { httpMock };
}

function listResponse(overrides: Partial<PaginatedResponse<QuoteListItem>> = {}): PaginatedResponse<QuoteListItem> {
  return {
    page: 1,
    size: 5,
    totalCount: 2,
    items: [
      { id: 1, author: 'Author A', textPreview: 'Preview A', authorQuoteCount: 1 },
      { id: 2, author: 'Author B', textPreview: 'Preview B', authorQuoteCount: 1 },
    ],
    ...overrides,
  };
}

function detailResponse(overrides: Partial<QuoteDetail> = {}): QuoteDetail {
  return { id: 1, author: 'Author A', text: 'Full text A', isDeleted: false, ...overrides };
}

function expectQuotesRequest(httpMock: HttpTestingController, page: number, size: number) {
  return httpMock.expectOne(
    (req) => req.url === '/api/quotes' && req.params.get('page') === String(page) && req.params.get('size') === String(size),
  );
}

describe('QuotesStore', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    ({ httpMock } = setUp());
  });

  afterEach(() => httpMock.verify());

  it('loads page 1 on construction and starts in the loading status', () => {
    const store = TestBed.inject(QuotesStore);

    expect(store.listStatus()).toBe('loading');
    expect(store.page()).toBe(1);

    expectQuotesRequest(httpMock, 1, 5).flush(listResponse());

    expect(store.listStatus()).toBe('loaded');
    expect(store.quotes()).toEqual(listResponse().items);
    expect(store.totalCount()).toBe(2);
  });

  it('moves to the empty status when the API returns no items', () => {
    const store = TestBed.inject(QuotesStore);

    expectQuotesRequest(httpMock, 1, 5).flush(listResponse({ items: [], totalCount: 0 }));

    expect(store.listStatus()).toBe('empty');
    expect(store.quotes()).toEqual([]);
  });

  it('moves to the error status and stores a mapped AppError on a failed request', () => {
    const store = TestBed.inject(QuotesStore);

    expectQuotesRequest(httpMock, 1, 5).flush(
      { title: 'Server error', status: 500 },
      { status: 500, statusText: 'Internal Server Error' },
    );

    expect(store.listStatus()).toBe('error');
    expect(store.listError()?.message).toBe('Server error');
  });

  it('loadPage requests the next page with the correct page/size params and updates state', () => {
    const store = TestBed.inject(QuotesStore);
    expectQuotesRequest(httpMock, 1, 5).flush(listResponse());

    store.loadPage(2);

    expect(store.listStatus()).toBe('loading');
    expectQuotesRequest(httpMock, 2, 5).flush(
      listResponse({ page: 2, items: [{ id: 3, author: 'Author C', textPreview: 'Preview C', authorQuoteCount: 1 }] }),
    );

    expect(store.page()).toBe(2);
    expect(store.listStatus()).toBe('loaded');
    expect(store.quotes()).toEqual([{ id: 3, author: 'Author C', textPreview: 'Preview C', authorQuoteCount: 1 }]);
  });

  it('ignores a stale list response when a newer page was requested before it resolved', () => {
    const store = TestBed.inject(QuotesStore);
    expectQuotesRequest(httpMock, 1, 5).flush(listResponse());

    store.loadPage(2);
    const page2Req = expectQuotesRequest(httpMock, 2, 5);
    store.loadPage(3);
    const page3Req = expectQuotesRequest(httpMock, 3, 5);

    // Resolve out of order: page 3 (the current page) first, then the now-stale page 2.
    page3Req.flush(listResponse({ page: 3, items: [{ id: 9, author: 'Nine', textPreview: 'p9', authorQuoteCount: 1 }] }));
    page2Req.flush(listResponse({ page: 2, items: [{ id: 8, author: 'Eight', textPreview: 'p8', authorQuoteCount: 1 }] }));

    expect(store.page()).toBe(3);
    expect(store.quotes()).toEqual([{ id: 9, author: 'Nine', textPreview: 'p9', authorQuoteCount: 1 }]);
  });

  it('selectQuote loads the detail for the given id', () => {
    const store = TestBed.inject(QuotesStore);
    expectQuotesRequest(httpMock, 1, 5).flush(listResponse());

    store.selectQuote(1);

    expect(store.detailStatus()).toBe('loading');
    expect(store.selectedId()).toBe(1);

    httpMock.expectOne('/api/quotes/1').flush(detailResponse());

    expect(store.detailStatus()).toBe('loaded');
    expect(store.detail()).toEqual(detailResponse());
  });

  it('sets the notfound status on a real 404 (empty body, matching GET /api/quotes/{id} for a missing id)', () => {
    const store = TestBed.inject(QuotesStore);
    expectQuotesRequest(httpMock, 1, 5).flush(listResponse());

    store.selectQuote(999999);

    httpMock.expectOne('/api/quotes/999999').flush(null, { status: 404, statusText: 'Not Found' });

    expect(store.detailStatus()).toBe('notfound');
  });

  it('ignores a stale detail response when a newer selection was made before it resolved', () => {
    const store = TestBed.inject(QuotesStore);
    expectQuotesRequest(httpMock, 1, 5).flush(listResponse());

    store.selectQuote(1);
    const req1 = httpMock.expectOne('/api/quotes/1');
    store.selectQuote(2);
    const req2 = httpMock.expectOne('/api/quotes/2');

    // Resolve out of order: quote 2 (the current selection) first, then the now-stale quote 1.
    req2.flush(detailResponse({ id: 2, author: 'Author B', text: 'Full text B' }));
    req1.flush(detailResponse({ id: 1, author: 'Author A', text: 'Full text A' }));

    expect(store.selectedId()).toBe(2);
    expect(store.detail()).toEqual(detailResponse({ id: 2, author: 'Author B', text: 'Full text B' }));
  });

  it('clearSelection resets the detail state back to idle', () => {
    const store = TestBed.inject(QuotesStore);
    expectQuotesRequest(httpMock, 1, 5).flush(listResponse());

    store.selectQuote(1);
    httpMock.expectOne('/api/quotes/1').flush(detailResponse());
    expect(store.detailStatus()).toBe('loaded');

    store.clearSelection();

    expect(store.selectedId()).toBeNull();
    expect(store.detail()).toBeNull();
    expect(store.detailStatus()).toBe('idle');
  });

  it('createQuote posts the request and emits the created quote', () => {
    const store = TestBed.inject(QuotesStore);
    expectQuotesRequest(httpMock, 1, 5).flush(listResponse());

    let emitted: QuoteDetail | undefined;
    store.createQuote({ author: 'Ada Lovelace', text: 'The Analytical Engine weaves algebra.' }).subscribe((q) => {
      emitted = q;
    });

    const req = httpMock.expectOne('/api/quotes');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ author: 'Ada Lovelace', text: 'The Analytical Engine weaves algebra.' });

    const created = detailResponse({ id: 7, author: 'Ada Lovelace', text: 'The Analytical Engine weaves algebra.' });
    req.flush(created, { status: 201, statusText: 'Created' });

    expect(emitted).toEqual(created);
    // The store's own side effect: refresh the current page so the new
    // quote is reflected without the caller having to know to ask for it.
    expectQuotesRequest(httpMock, 1, 5).flush(listResponse());
  });

  it('createQuote propagates a mapped AppError and does not refresh the list on failure', () => {
    const store = TestBed.inject(QuotesStore);
    expectQuotesRequest(httpMock, 1, 5).flush(listResponse());

    let caught: unknown;
    store.createQuote({ author: '', text: 'Valid text' }).subscribe({
      error: (err) => {
        caught = err;
      },
    });

    httpMock
      .expectOne('/api/quotes')
      .flush(
        { title: 'One or more validation errors occurred.', errors: { Author: ['The Author field is required.'] } },
        { status: 400, statusText: 'Bad Request' },
      );

    expect((caught as { kind: string })?.kind).toBe('validation');
    httpMock.expectNone((req) => req.url === '/api/quotes' && req.method === 'GET');
  });
});
