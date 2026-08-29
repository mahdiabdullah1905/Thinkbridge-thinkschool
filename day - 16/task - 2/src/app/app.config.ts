import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { authInterceptor } from './core/interceptors/auth-interceptor';
import { errorMappingInterceptor } from './core/interceptors/error-mapping-interceptor';
import { retryInterceptor } from './core/interceptors/retry-interceptor';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    // Binds route params (e.g. :id) directly to component inputs - see
    // features/quote-detail/quote-detail.ts.
    provideRouter(routes, withComponentInputBinding()),
    // Same chain and ordering as day - 16/task - 1: auth outermost, error-mapping
    // in the middle (converts only the final post-retry failure), retry innermost.
    provideHttpClient(
      withFetch(),
      withInterceptors([authInterceptor, errorMappingInterceptor, retryInterceptor]),
    ),
  ],
};
