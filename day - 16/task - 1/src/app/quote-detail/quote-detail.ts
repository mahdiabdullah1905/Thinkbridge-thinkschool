import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AppError } from '../errors/app-error';
import { QuoteDetail as QuoteDetailModel, QuotesApi } from '../quotes-api';

type DetailStatus = 'loading' | 'error' | 'notfound' | 'success';

@Component({
  selector: 'app-quote-detail',
  imports: [RouterLink],
  templateUrl: './quote-detail.html',
  styleUrl: './quote-detail.css',
})
export class QuoteDetail {
  private readonly quotesApi = inject(QuotesApi);

  // Bound to the :id route param via withComponentInputBinding() in
  // app.config.ts - route params always arrive as strings.
  readonly id = input.required<string>();

  private readonly quoteId = computed(() => Number(this.id()));

  protected readonly quote = signal<QuoteDetailModel | null>(null);
  protected readonly status = signal<DetailStatus>('loading');
  protected readonly error = signal<AppError | null>(null);

  constructor() {
    effect(() => {
      const requestedId = this.quoteId();

      this.status.set('loading');
      this.error.set(null);

      // untracked(): see quote-list.ts for why the HTTP call itself must not
      // run inside the effect's tracked scope (the auth interceptor reads a
      // signal synchronously during .subscribe()).
      untracked(() => {
        this.quotesApi.getQuoteById(requestedId).subscribe({
          next: (response) => {
            if (this.quoteId() !== requestedId) return;
            this.quote.set(response);
            this.status.set('success');
          },
          error: (err: AppError) => {
            if (this.quoteId() !== requestedId) return;
            this.error.set(err);
            this.status.set(err.status === 404 ? 'notfound' : 'error');
          },
        });
      });
    });
  }
}
