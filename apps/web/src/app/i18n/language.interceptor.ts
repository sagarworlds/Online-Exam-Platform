import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { environment } from '../../environments/environment';
import { I18nService } from './i18n.service';

/**
 * Tells the API which language the user chose, in `Accept-Language`, so a candidate is shown questions in that language where a
 * translation exists (FR-51). The browser sends its own language list anyway; this replaces it with the user's choice, which is the one
 * that counts. Only requests to our own API get it, so the choice is not handed to other sites.
 */
export const languageInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith(environment.apiBaseUrl)) {
    return next(request);
  }

  return next(request.clone({ setHeaders: { 'Accept-Language': inject(I18nService).language() } }));
};
