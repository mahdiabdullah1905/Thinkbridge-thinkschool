import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthApi } from '../auth-api';
import { AuthTokenStore } from '../auth-token-store';
import { AppError } from '../errors/app-error';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class Login {
  private readonly authApi = inject(AuthApi);
  private readonly tokenStore = inject(AuthTokenStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly pending = signal(false);
  protected readonly error = signal<AppError | null>(null);

  protected login(): void {
    this.pending.set(true);
    this.error.set(null);

    this.authApi.login({ email: this.email(), password: this.password() }).subscribe({
      next: (response) => {
        this.tokenStore.setToken(response.accessToken);
        this.pending.set(false);
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/quotes';
        this.router.navigateByUrl(returnUrl);
      },
      error: (err: AppError) => {
        this.error.set(err);
        this.pending.set(false);
      },
    });
  }
}
