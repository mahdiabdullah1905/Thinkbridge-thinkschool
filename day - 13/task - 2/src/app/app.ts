import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { QuoteDetail, QuoteListItem, QuotesApi } from './quotes-api';

type ListStatus = 'loading' | 'error' | 'empty' | 'success';
type DetailStatus = 'idle' | 'loading' | 'error' | 'success';

@Component({
  selector: 'app-root',
  imports: [],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly quotesApi = inject(QuotesApi);

  // --- list state ---
  protected readonly page = signal(1);
  protected readonly pageSize = signal(5);
  protected readonly quotes = signal<QuoteListItem[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly listLoading = signal(false);
  protected readonly listError = signal<string | null>(null);

  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize())),
  );
  protected readonly canGoPrevious = computed(() => this.page() > 1);
  protected readonly canGoNext = computed(() => this.page() < this.totalPages());

  protected readonly listStatus = computed<ListStatus>(() => {
    if (this.listLoading()) return 'loading';
    if (this.listError()) return 'error';
    return this.quotes().length === 0 ? 'empty' : 'success';
  });

  // --- detail state ---
  protected readonly selectedId = signal<number | null>(null);
  protected readonly detail = signal<QuoteDetail | null>(null);
  protected readonly detailLoading = signal(false);
  protected readonly detailError = signal<string | null>(null);

  protected readonly detailStatus = computed<DetailStatus>(() => {
    if (this.selectedId() === null) return 'idle';
    if (this.detailLoading()) return 'loading';
    if (this.detailError()) return 'error';
    return 'success';
  });

  constructor() {
    // Fetching the list page is a side effect, not a derived value - it
    // belongs in effect(), not computed(). Fires once immediately on
    // startup (page = 1) because it reads `page`/`pageSize` synchronously.
    effect(() => {
      const requestedPage = this.page();
      const size = this.pageSize();

      this.listLoading.set(true);
      this.listError.set(null);

      this.quotesApi.getQuotes(requestedPage, size).subscribe({
        next: (response) => {
          if (this.page() !== requestedPage) return; // a newer page was requested meanwhile
          this.quotes.set(response.items);
          this.totalCount.set(response.totalCount);
          this.listLoading.set(false);
        },
        error: (err: HttpErrorResponse) => {
          if (this.page() !== requestedPage) return;
          console.error(`Failed to load quotes (page ${requestedPage}):`, err);
          this.listError.set('Could not reach the quotes API. Is it running?');
          this.listLoading.set(false);
        },
      });
    });

    // Same shape as the list effect above, but for the detail request. The
    // guard here is the one this task is actually about: if the user selects
    // quote A and then quote B before A's response comes back, A's response
    // must not overwrite B's already-newer selection. Comparing
    // `this.selectedId()` against the id this particular request was made
    // for is what makes that safe.
    effect(() => {
      const requestedId = this.selectedId();

      if (requestedId === null) {
        this.detail.set(null);
        this.detailError.set(null);
        this.detailLoading.set(false);
        return;
      }

      this.detailLoading.set(true);
      this.detailError.set(null);

      this.quotesApi.getQuoteById(requestedId).subscribe({
        next: (response) => {
          if (this.selectedId() !== requestedId) return; // a newer selection arrived meanwhile
          this.detail.set(response);
          this.detailLoading.set(false);
        },
        error: (err: HttpErrorResponse) => {
          if (this.selectedId() !== requestedId) return;
          console.error(`Failed to load quote ${requestedId}:`, err);
          this.detailError.set(
            err.status === 404 ? 'That quote no longer exists.' : 'Could not load that quote.',
          );
          this.detailLoading.set(false);
        },
      });
    });
  }

  protected goToPage(target: number): void {
    this.page.set(Math.min(Math.max(1, target), this.totalPages()));
  }

  // Deliberately does NOT clamp to totalPages, so typing a page number past
  // the end is a real way to trigger the empty state instead of a fake one.
  protected jumpToPage(rawValue: string): void {
    const parsed = Number(rawValue);
    if (Number.isInteger(parsed) && parsed > 0) {
      this.page.set(parsed);
    }
  }

  protected selectQuote(id: number): void {
    this.selectedId.set(id);
  }
}
