import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners, isDevMode } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import { authInterceptor } from './auth/auth.interceptor';
import { languageInterceptor } from './i18n/language.interceptor';
import { deviceSignatureInterceptor } from './shared/device/device-signature.interceptor';
import { apiActivityInterceptor } from './shared/api-activity/api-activity.interceptor';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withFetch(), withInterceptors([apiActivityInterceptor, deviceSignatureInterceptor, languageInterceptor, authInterceptor])),
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};
