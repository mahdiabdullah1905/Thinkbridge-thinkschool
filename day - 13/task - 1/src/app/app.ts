import { Component, computed, effect, inject, signal } from '@angular/core';
import { QuoteListItem, QuotesApi } from './quotes-api';

type Status = 'loading' | 'error' | 'empty' | 'success';

@Component({
  selector: 'app-root',
  imports: [],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly quotesApi = inject(QuotesApi);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(5);

  protected readonly quotes = signal<QuoteListItem[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize())),
  );
  protected readonly canGoPrevious = computed(() => this.page() > 1);
  protected readonly canGoNext = computed(() => this.page() < this.totalPages());

  // status is a pure combination of loading/error/quotes - a textbook computed().
  protected readonly status = computed<Status>(() => {
    if (this.loading()) return 'loading';
    if (this.error()) return 'error';
    return this.quotes().length === 0 ? 'empty' : 'success';
  });

  constructor() {
    // Calling the API is a side effect, not a derived value, so it belongs in
    // effect() rather than computed(). This effect re-reads `page`/`pageSize`,
    // which means it also fires once immediately on startup (page = 1). The
    // signal writes in the subscribe callback below are what actually update
    // the screen - there's no zone.js patch involved anywhere in that chain.
    effect(() => {
      const requestedPage = this.page();
      const size = this.pageSize();

      this.loading.set(true);
      this.error.set(null);

      this.quotesApi.getQuotes(requestedPage, size).subscribe({
        next: (response) => {
          if (this.page() !== requestedPage) return; // a newer page was requested meanwhile
          this.quotes.set(response.items);
          this.totalCount.set(response.totalCount);
          this.loading.set(false);
        },
        error: () => {
          if (this.page() !== requestedPage) return;
          this.error.set('Could not reach the quotes API. Is it running?');
          this.loading.set(false);
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
}
