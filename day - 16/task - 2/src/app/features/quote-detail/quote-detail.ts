import { Component, computed, effect, inject, input, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { QuotesStore } from '../../state/quotes-store/quotes-store';

@Component({
  selector: 'app-quote-detail',
  imports: [RouterLink],
  templateUrl: './quote-detail.html',
  styleUrl: './quote-detail.css',
})
export class QuoteDetail {
  protected readonly store = inject(QuotesStore);

  // Bound to the :id route param via withComponentInputBinding() in app.config.ts.
  readonly id = input.required<string>();
  private readonly quoteId = computed(() => Number(this.id()));

  constructor() {
    // Angular reuses this component instance across /quotes/1 -> /quotes/2
    // navigations (same route config), so selecting on id change has to be
    // reactive, not a one-shot constructor call. untracked(): the store's
    // HTTP call goes through authInterceptor, which reads AuthTokenStore's
    // token signal synchronously during .subscribe() - without untracked()
    // that read would be attributed to this effect as a dependency.
    effect(() => {
      const id = this.quoteId();
      untracked(() => this.store.selectQuote(id));
    });
  }
}
