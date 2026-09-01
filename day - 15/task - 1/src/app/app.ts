import { Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthApi } from './auth-api';
import { AuthTokenStore } from './auth-token-store';
import { AppError } from './errors/app-error';
import { QuoteListItem, QuotesApi } from './quotes-api';

type ListStatus = 'loading' | 'error' | 'empty' | 'success';

@Component({
  selector: 'app-root',
  imports: [FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly quotesApi = inject(QuotesApi);
  private readonly authApi = inject(AuthApi);
  private readonly tokenStore = inject(AuthTokenStore);

  protected readonly quotes = signal<QuoteListItem[]>([]);
  protected readonly listLoading = signal(true);
  protected readonly listError = signal<AppError | null>(null);
  protected readonly listStatus = signal<ListStatus>('loading');

  protected readonly email = signal('test@example.com');
  protected readonly password = signal('');
  protected readonly loginPending = signal(false);
  protected readonly loginError = signal<AppError | null>(null);
  protected readonly signedIn = signal(false);

  protected readonly objectKeys = Object.keys;

  constructor() {
    effect(() => {
      this.listLoading.set(true);
      this.listError.set(null);

      // The auth interceptor reads AuthTokenStore's token signal while this
      // effect is on the call stack (HttpClient runs interceptors
      // synchronously during .subscribe()). Without untracked(), that read
      // gets attributed to THIS effect as a dependency, so every future
      // sign-in/sign-out would silently re-trigger the whole quotes fetch -
      // caught by a test asserting the Authorization header on a *second*
      // request, which found an unexpected extra GET /api/quotes.
      untracked(() => {
        this.quotesApi.getQuotes(1, 5).subscribe({
          next: (response) => {
            this.quotes.set(response.items);
            this.listStatus.set(response.items.length === 0 ? 'empty' : 'success');
            this.listLoading.set(false);
          },
          // The error-mapping interceptor has already turned this into an
          // AppError by the time it reaches here - no ProblemDetails parsing
          // in the component.
          error: (err: AppError) => {
            this.listError.set(err);
            this.listStatus.set('error');
            this.listLoading.set(false);
          },
        });
      });
    });
  }

  protected login(): void {
    this.loginPending.set(true);
    this.loginError.set(null);

    this.authApi.login({ email: this.email(), password: this.password() }).subscribe({
      next: (response) => {
        this.tokenStore.setToken(response.accessToken);
        this.signedIn.set(true);
        this.loginPending.set(false);
      },
      error: (err: AppError) => {
        this.loginError.set(err);
        this.loginPending.set(false);
      },
    });
  }
}
