import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AppError } from '../errors/app-error';
import { QuoteListItem, QuotesApi } from '../quotes-api';

type ListStatus = 'loading' | 'error' | 'empty' | 'success';

@Component({
  selector: 'app-quote-list',
  imports: [RouterLink],
  templateUrl: './quote-list.html',
  styleUrl: './quote-list.css',
})
export class QuoteList {
  private readonly quotesApi = inject(QuotesApi);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(5);
  protected readonly quotes = signal<QuoteListItem[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly loading = signal(false);
  protected readonly error = signal<AppError | null>(null);

  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize())),
  );
  protected readonly canGoPrevious = computed(() => this.page() > 1);
  protected readonly canGoNext = computed(() => this.page() < this.totalPages());

  protected readonly status = computed<ListStatus>(() => {
    if (this.loading()) return 'loading';
    if (this.error()) return 'error';
    return this.quotes().length === 0 ? 'empty' : 'success';
  });

  constructor() {
    effect(() => {
      const requestedPage = this.page();
      const size = this.pageSize();

      this.loading.set(true);
      this.error.set(null);

      // untracked(): the auth interceptor reads AuthTokenStore's token
      // signal synchronously during .subscribe(). Without this, that read
      // gets attributed to this effect as a dependency, and every future
      // sign-in/sign-out would silently re-trigger this fetch.
      untracked(() => {
        this.quotesApi.getQuotes(requestedPage, size).subscribe({
          next: (response) => {
            if (this.page() !== requestedPage) return;
            this.quotes.set(response.items);
            this.totalCount.set(response.totalCount);
            this.loading.set(false);
          },
          error: (err: AppError) => {
            if (this.page() !== requestedPage) return;
            this.error.set(err);
            this.loading.set(false);
          },
        });
      });
    });
  }

  protected goToPage(target: number): void {
    this.page.set(Math.min(Math.max(1, target), this.totalPages()));
  }
}
