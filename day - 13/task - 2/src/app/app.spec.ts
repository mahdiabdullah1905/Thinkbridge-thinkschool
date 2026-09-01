import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { App } from './app';
import { PaginatedResponse, QuoteDetail, QuoteListItem } from './quotes-api';

const LIST_RESPONSE: PaginatedResponse<QuoteListItem> = {
  page: 1,
  size: 5,
  totalCount: 2,
  items: [
    { id: 1, author: 'Author A', textPreview: 'Quote A preview', authorQuoteCount: 1 },
    { id: 2, author: 'Author B', textPreview: 'Quote B preview', authorQuoteCount: 1 },
  ],
};

describe('App', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
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
    expect(fixture.nativeElement.textContent).toContain('Quote B preview');
  });

  it('shows the empty state when a page has no items', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.autoDetectChanges();
    await fixture.whenStable();

    const response: PaginatedResponse<QuoteListItem> = { page: 500, size: 5, totalCount: 0, items: [] };
    httpMock.expectOne((req) => req.url === '/api/quotes').flush(response);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('No quotes on page');
  });

  it('shows the list error state when the request fails', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.autoDetectChanges();
    await fixture.whenStable();

    httpMock.expectOne((req) => req.url === '/api/quotes').error(new ProgressEvent('error'));
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Could not reach the quotes API');
  });

  it('shows a quote detail after it is selected from the list', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.autoDetectChanges();
    await fixture.whenStable();

    httpMock.expectOne((req) => req.url === '/api/quotes').flush(LIST_RESPONSE);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Select a quote to see its details');

    fixture.nativeElement.querySelector('.quote-button').click();
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Loading quote');

    const detail: QuoteDetail = { id: 1, author: 'Author A', text: 'Full text of quote A', isDeleted: false };
    httpMock.expectOne('/api/quotes/1').flush(detail);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Full text of quote A');
  });

  it('shows the detail error state when the detail request fails', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.autoDetectChanges();
    await fixture.whenStable();

    httpMock.expectOne((req) => req.url === '/api/quotes').flush(LIST_RESPONSE);
    await fixture.whenStable();

    fixture.nativeElement.querySelector('.quote-button').click();
    await fixture.whenStable();

    httpMock.expectOne('/api/quotes/1').error(new ProgressEvent('error'));
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Could not load that quote');
  });

  // GET /api/quotes/{id} genuinely 404s for an id that doesn't exist (verified
  // with curl against the real API) - that's a different situation from the
  // server being unreachable, and deserves a different message.
  it('shows a not-found message specifically when the detail request 404s', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.autoDetectChanges();
    await fixture.whenStable();

    httpMock.expectOne((req) => req.url === '/api/quotes').flush(LIST_RESPONSE);
    await fixture.whenStable();

    fixture.nativeElement.querySelector('.quote-button').click();
    await fixture.whenStable();

    httpMock.expectOne('/api/quotes/1').flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('That quote no longer exists');
  });

  // This is the actual case the task cares about: a list request and a
  // detail request overlapping, where an OLDER detail response arrives
  // after a NEWER selection was already made. A live network can't be
  // made to reorder responses on demand, so this proves it deterministically
  // by resolving the two mocked detail requests out of order.
  it('ignores a stale detail response when a newer quote was selected before it resolved', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.autoDetectChanges();
    await fixture.whenStable();

    httpMock.expectOne((req) => req.url === '/api/quotes').flush(LIST_RESPONSE);
    await fixture.whenStable();

    const buttons: NodeListOf<HTMLButtonElement> =
      fixture.nativeElement.querySelectorAll('.quote-button');
    expect(buttons.length).toBe(2);

    buttons[0].click(); // select quote 1
    await fixture.whenStable();
    const req1 = httpMock.expectOne('/api/quotes/1');

    buttons[1].click(); // select quote 2 before quote 1's response has arrived
    await fixture.whenStable();
    const req2 = httpMock.expectOne('/api/quotes/2');

    // Resolve the NEWER request first, the way a real out-of-order response would.
    req2.flush({ id: 2, author: 'Author B', text: 'Full text of quote B', isDeleted: false });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Full text of quote B');

    // The STALE, older request resolves after - it must be ignored.
    req1.flush({ id: 1, author: 'Author A', text: 'Full text of quote A', isDeleted: false });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Full text of quote B');
    expect(fixture.nativeElement.textContent).not.toContain('Full text of quote A');
  });
});
