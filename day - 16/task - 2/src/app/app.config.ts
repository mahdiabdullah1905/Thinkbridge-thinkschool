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
    // Same chain and ordering as day - 16/task - 1: auth outermost, error-mapping
    // in the middle (converts only the final post-retry failure), retry innermost.
    provideHttpClient(withInterceptors([authInterceptor, errorMappingInterceptor, retryInterceptor])),
  ],
};
