import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthTokenStore } from '../auth-token-store';

// The real API doesn't require a token for GET /api/quotes or GET
// /api/quotes/{id} (see day - 2/QuotesApi/Extensions/ProgramExtensions.cs -
// only the POST/DELETE quote endpoints call .RequireAuthorization()). This
// guard is therefore an app-level access rule, not a mirror of a server-side
// 401: viewing quote detail is gated behind sign-in so there is a real,
// testable redirect for an unauthenticated user, while the list stays public.
export const authGuard: CanActivateFn = (route, state) => {
  const tokenStore = inject(AuthTokenStore);
  const router = inject(Router);

  if (tokenStore.currentToken()) {
    return true;
  }

  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};
