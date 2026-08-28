import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthTokenStore } from '../auth-token-store';

// Unlike Day 16 Task 1's guard (which gated GET /api/quotes/:id even though
// the real API doesn't require auth for it), this one guards "quotes/new",
// which maps to POST /api/quotes - and that endpoint genuinely carries
// .RequireAuthorization() server-side (ProgramExtensions.cs). An
// unauthenticated POST would 401 at the API regardless; this guard just
// gives the user a real sign-in redirect instead of a failed submission.
export const authGuard: CanActivateFn = (route, state) => {
  const tokenStore = inject(AuthTokenStore);
  const router = inject(Router);

  if (tokenStore.currentToken()) {
    return true;
  }

  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};
