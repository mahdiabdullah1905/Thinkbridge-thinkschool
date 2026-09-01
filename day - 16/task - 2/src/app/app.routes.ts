import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { QuoteList } from './features/quote-list/quote-list';

// Mirrors the backend's own resource grouping (day - 2/QuotesApi/Extensions/
// ProgramExtensions.cs, MapQuoteEndpoints -> app.MapGroup("/api/quotes")):
//   GET  /api/quotes      -> 'quotes'      (index)
//   POST /api/quotes      -> 'quotes/new'  (create form)
//   GET  /api/quotes/{id} -> 'quotes/:id'  (show)
// "new" is registered before ":id" so the router doesn't try to resolve it
// as an id first.
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'quotes' },

  // Eager: ships in the initial bundle - this is the app's index/home route.
  { path: 'quotes', component: QuoteList },

  // Lazy, and guarded: POST /api/quotes genuinely carries .RequireAuthorization()
  // server-side (unlike quotes/:id below), so this guard mirrors a real backend
  // rule rather than being a purely client-side choice.
  {
    path: 'quotes/new',
    loadComponent: () => import('./features/create-quote/create-quote').then((m) => m.CreateQuote),
    canActivate: [authGuard],
  },

  // Lazy: GET /api/quotes/{id} has no server-side auth requirement, so this
  // route is intentionally left unguarded.
  {
    path: 'quotes/:id',
    loadComponent: () => import('./features/quote-detail/quote-detail').then((m) => m.QuoteDetail),
  },

  { path: 'login', loadComponent: () => import('./features/login/login').then((m) => m.Login) },

  { path: '**', redirectTo: 'quotes' },
];
