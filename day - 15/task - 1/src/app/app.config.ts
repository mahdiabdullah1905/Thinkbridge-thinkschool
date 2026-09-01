import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { authInterceptor } from './interceptors/auth-interceptor';
import { errorMappingInterceptor } from './interceptors/error-mapping-interceptor';
import { retryInterceptor } from './interceptors/retry-interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
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
