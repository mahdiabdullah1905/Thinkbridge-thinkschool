import { Injectable, signal } from '@angular/core';

// In-memory only: the token lives for the lifetime of the tab. Swapping this
// for localStorage/sessionStorage later doesn't change the interceptor.
@Injectable({
  providedIn: 'root',
})
export class AuthTokenStore {
  private readonly token = signal<string | null>(null);

  readonly currentToken = this.token.asReadonly();

  setToken(accessToken: string): void {
    this.token.set(accessToken);
  }

  clear(): void {
    this.token.set(null);
  }
}
