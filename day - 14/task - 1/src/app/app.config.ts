import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideHttpClient } from '@angular/common/http';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // No zone.js anywhere in this app - Angular schedules change detection
    // from signal writes instead of from patched async APIs.
    provideZonelessChangeDetection(),
    provideHttpClient(),
  ],
};
