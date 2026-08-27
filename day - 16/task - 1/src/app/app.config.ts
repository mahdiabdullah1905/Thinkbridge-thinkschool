import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withViewTransitions } from '@angular/router';
import { routes } from './app.routes';
import { authInterceptor } from './interceptors/auth-interceptor';
import { errorMappingInterceptor } from './interceptors/error-mapping-interceptor';
import { retryInterceptor } from './interceptors/retry-interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(
      routes,
      // Binds route params (e.g. :id) directly to component inputs, so
      // QuoteDetail can use input() instead of reading ActivatedRoute by hand.
      withComponentInputBinding(),
      // Wraps every router navigation in the browser's View Transition API
      // when supported, including the list -> detail navigation below.
      withViewTransitions(),
    ),
    // Order matters: interceptors run in this order on the way out and in
    // reverse on the way back (each wraps the next, backend innermost).
    //   auth          - attaches the Bearer header before anything is sent.
    //   errorMapping  - sits outside retry, so it only converts the FINAL
    //                   post-retry failure into an AppError.
    //   retry         - closest to the backend, retries idempotent GETs
    //                   against the raw HttpErrorResponse.
    provideHttpClient(withInterceptors([authInterceptor, errorMappingInterceptor, retryInterceptor])),
  ],
};
