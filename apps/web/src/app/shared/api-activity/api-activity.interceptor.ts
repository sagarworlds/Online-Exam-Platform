import { HttpContextToken, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { finalize } from 'rxjs';
import { ApiActivityService } from './api-activity.service';

/**
 * Set on a request that is already reflected on screen and saved in the background, such as an exam answer
 * that shows at once. Such a call never shows the waiting indicator: it would flash on every click and
 * pull a candidate's attention away from the question.
 */
export const SILENT_ACTIVITY = new HttpContextToken<boolean>(() => false);

/** Reports each API call to {@link ApiActivityService}, so the page can show that it is waiting on the server. */
export const apiActivityInterceptor: HttpInterceptorFn = (request, next) => {
  if (request.context.get(SILENT_ACTIVITY)) {
    return next(request);
  }

  const activity = inject(ApiActivityService);
  activity.begin();
  // finalize also runs when the caller unsubscribes (a cancelled request), so a call can never be left counted.
  return next(request).pipe(finalize(() => activity.end()));
};
