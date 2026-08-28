import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthTokenStore } from '../auth-token-store';

// /api/auth/login and /api/auth/refresh are how a token is obtained in the
// first place - sending a stale/absent Bearer token on those is pointless
// and would only confuse the 401 they'd otherwise return on bad credentials.
const UNAUTHENTICATED_PATHS = ['/api/auth/login', '/api/auth/refresh'];

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const tokenStore = inject(AuthTokenStore);
  const token = tokenStore.currentToken();

  if (!token || UNAUTHENTICATED_PATHS.some((path) => req.url.includes(path))) {
    return next(req);
  }

  return next(
    req.clone({
      setHeaders: { Authorization: `Bearer ${token}` },
    }),
  );
};
