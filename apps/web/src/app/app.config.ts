import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, isDevMode, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { TitleStrategy, provideRouter } from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import { PageTitleStrategy } from './shared/page-title/page-title.strategy';
import { authInterceptor } from './auth/auth.interceptor';
import { I18nService } from './i18n/i18n.service';
import { languageInterceptor } from './i18n/language.interceptor';
import { deviceSignatureInterceptor } from './shared/device/device-signature.interceptor';
import { apiActivityInterceptor } from './shared/api-activity/api-activity.interceptor';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // The chosen language's messages are fetched before the first screen is drawn (see I18nService.ready).
    provideAppInitializer(() => inject(I18nService).ready()),
    provideRouter(routes),
    // Names each page in the browser tab (WCAG 2.4.2); see PageTitleStrategy.
    { provide: TitleStrategy, useClass: PageTitleStrategy },
    provideHttpClient(withFetch(), withInterceptors([apiActivityInterceptor, deviceSignatureInterceptor, languageInterceptor, authInterceptor])),
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};
