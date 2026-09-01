import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { AuthTokenStore } from '../auth-token-store';
import { authGuard } from './auth.guard';

describe('authGuard', () => {
  let tokenStore: AuthTokenStore;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([])],
    });
    tokenStore = TestBed.inject(AuthTokenStore);
    router = TestBed.inject(Router);
  });

  function runGuard(url: string) {
    return TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
    );
  }

  it('redirects to /login with a returnUrl when there is no token', () => {
    tokenStore.clear();

    const result = runGuard('/quotes/new') as UrlTree;

    expect(result).toBeInstanceOf(UrlTree);
    expect(router.serializeUrl(result)).toBe('/login?returnUrl=%2Fquotes%2Fnew');
  });

  it('allows activation when a token is present', () => {
    tokenStore.setToken('a-token');

    const result = runGuard('/quotes/new');

    expect(result).toBe(true);
  });
});
