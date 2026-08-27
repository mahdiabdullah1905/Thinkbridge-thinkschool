import { Component, inject } from '@angular/core';
import { QuotesStore } from './quotes-store/quotes-store';

@Component({
  selector: 'app-root',
  imports: [],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  // The component holds no state of its own - it only reads QuotesStore's
  // signals and calls its methods. All list/detail state lives in the store.
  protected readonly store = inject(QuotesStore);

  protected goToPage(target: number): void {
    this.store.loadPage(Math.min(Math.max(1, target), this.store.totalPages()));
  }

  protected selectQuote(id: number): void {
    this.store.selectQuote(id);
  }
}
