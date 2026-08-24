import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { App } from './app';
import { PaginatedResponse, QuoteListItem } from './quotes-api';

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

  it('shows a loading state, then renders quotes once the API responds', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.autoDetectChanges();
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Loading quotes');

    const response: PaginatedResponse<QuoteListItem> = {
      page: 1,
      size: 5,
      totalCount: 1,
      items: [{ id: 1, author: 'Ada Lovelace', textPreview: 'Test quote', authorQuoteCount: 1 }],
    };
    httpMock.expectOne((req) => req.url === '/api/quotes').flush(response);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Ada Lovelace');
  });

  it('shows the empty state when a page has no items', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.autoDetectChanges();
    await fixture.whenStable();

    const response: PaginatedResponse<QuoteListItem> = {
      page: 500,
      size: 5,
      totalCount: 0,
      items: [],
    };
    httpMock.expectOne((req) => req.url === '/api/quotes').flush(response);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('No quotes on page');
  });

  it('shows the error state when the request fails', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.autoDetectChanges();
    await fixture.whenStable();

    httpMock.expectOne((req) => req.url === '/api/quotes').error(new ProgressEvent('error'));
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Could not reach the quotes API');
  });
});
