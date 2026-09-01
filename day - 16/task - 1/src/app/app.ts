import { Component, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { AuthTokenStore } from './auth-token-store';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly tokenStore = inject(AuthTokenStore);

  protected signOut(): void {
    this.tokenStore.clear();
  }
}
