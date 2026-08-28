import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { AppError } from '../../core/errors/app-error';
import { CreateQuoteRequest, QuoteDetail, QuoteListItem, QuotesApi } from '../../core/quotes-api';

export type ListStatus = 'idle' | 'loading' | 'loaded' | 'empty' | 'error';
export type DetailStatus = 'idle' | 'loading' | 'loaded' | 'notfound' | 'error';

const DEFAULT_PAGE_SIZE = 5;

// Plain signals + an injectable service - no NgRx, no signal-store. See
// README.md for where this stops being enough.
@Injectable({
  providedIn: 'root',
})
export class QuotesStore {
  private readonly quotesApi = inject(QuotesApi);

  // --- list state ---
  private readonly _page = signal(1);
  private readonly _pageSize = signal(DEFAULT_PAGE_SIZE);
  private readonly _quotes = signal<QuoteListItem[]>([]);
  private readonly _totalCount = signal(0);
  private readonly _listStatus = signal<ListStatus>('idle');
  private readonly _listError = signal<AppError | null>(null);

  readonly page = this._page.asReadonly();
  readonly pageSize = this._pageSize.asReadonly();
  readonly quotes = this._quotes.asReadonly();
  readonly totalCount = this._totalCount.asReadonly();
  readonly listStatus = this._listStatus.asReadonly();
  readonly listError = this._listError.asReadonly();

  readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this._totalCount() / this._pageSize())),
  );
  readonly canGoPrevious = computed(() => this._page() > 1);
  readonly canGoNext = computed(() => this._page() < this.totalPages());

  // --- detail/selection state ---
  private readonly _selectedId = signal<number | null>(null);
  private readonly _detail = signal<QuoteDetail | null>(null);
  private readonly _detailStatus = signal<DetailStatus>('idle');
  private readonly _detailError = signal<AppError | null>(null);

  readonly selectedId = this._selectedId.asReadonly();
  readonly detail = this._detail.asReadonly();
  readonly detailStatus = this._detailStatus.asReadonly();
  readonly detailError = this._detailError.asReadonly();

  constructor() {
    this.loadPage(1);
  }

  loadPage(page: number): void {
    const targetPage = Math.max(1, page);

    this._page.set(targetPage);
    this._listStatus.set('loading');
    this._listError.set(null);

    this.quotesApi.getQuotes(targetPage, this._pageSize()).subscribe({
      next: (response) => {
        // A newer loadPage() call superseded this one before it resolved -
        // the stale response must not overwrite the current page's data.
        if (this._page() !== targetPage) return;
        this._quotes.set(response.items);
        this._totalCount.set(response.totalCount);
        this._listStatus.set(response.items.length === 0 ? 'empty' : 'loaded');
      },
      error: (err: AppError) => {
        if (this._page() !== targetPage) return;
        this._listError.set(err);
        this._listStatus.set('error');
      },
    });
  }

  selectQuote(id: number): void {
    this._selectedId.set(id);
    this._detail.set(null);
    this._detailStatus.set('loading');
    this._detailError.set(null);

    this.quotesApi.getQuoteById(id).subscribe({
      next: (response) => {
        // Same stale-response guard as loadPage(): a later selectQuote()
        // call must win over an earlier one that resolves after it.
        if (this._selectedId() !== id) return;
        this._detail.set(response);
        this._detailStatus.set('loaded');
      },
      error: (err: AppError) => {
        if (this._selectedId() !== id) return;
        this._detailError.set(err);
        this._detailStatus.set(err.status === 404 ? 'notfound' : 'error');
      },
    });
  }

  clearSelection(): void {
    this._selectedId.set(null);
    this._detail.set(null);
    this._detailStatus.set('idle');
    this._detailError.set(null);
  }

  // Creating a quote is a one-shot form submission - the caller (the
  // create-quote form) owns the resulting UX (validation feedback, focus,
  // navigation), so this returns the Observable rather than adding a third
  // status signal here that only one component would ever read. What the
  // store DOES own is the side effect only it can perform: refreshing the
  // current page so a newly created quote actually shows up in the list.
  createQuote(request: CreateQuoteRequest): Observable<QuoteDetail> {
    return this.quotesApi.createQuote(request).pipe(tap(() => this.loadPage(this._page())));
  }
}
