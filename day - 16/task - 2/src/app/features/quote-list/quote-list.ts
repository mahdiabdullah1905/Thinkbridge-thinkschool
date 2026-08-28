import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { QuotesStore } from '../../state/quotes-store/quotes-store';

@Component({
  selector: 'app-quote-list',
  imports: [RouterLink],
  templateUrl: './quote-list.html',
  styleUrl: './quote-list.css',
})
export class QuoteList {
  // No local state - reads and drives QuotesStore directly.
  protected readonly store = inject(QuotesStore);

  protected goToPage(target: number): void {
    this.store.loadPage(Math.min(Math.max(1, target), this.store.totalPages()));
  }
}
