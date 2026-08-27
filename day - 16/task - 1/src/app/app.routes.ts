import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';
import { QuoteList } from './quote-list/quote-list';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'quotes' },

  // Eager: ships in the initial bundle, so there is a real contrast with the
  // lazy-loaded detail route below to verify in the Network tab.
  { path: 'quotes', component: QuoteList },

  // Lazy: only fetched as its own chunk when the user actually navigates to
  // a detail route. Gated behind authGuard - see auth/auth.guard.ts for why
  // this is an app-level rule rather than a mirror of the API's own auth.
  {
    path: 'quotes/:id',
    loadComponent: () => import('./quote-detail/quote-detail').then((m) => m.QuoteDetail),
    canActivate: [authGuard],
  },

  { path: 'login', loadComponent: () => import('./login/login').then((m) => m.Login) },

  { path: '**', redirectTo: 'quotes' },
];
