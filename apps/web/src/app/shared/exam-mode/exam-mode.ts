import { Injectable, signal } from '@angular/core';

/**
 * Whether a candidate is sitting an exam right now. The exam page sets it while an attempt is open, and the page around it (app.html) puts
 * its header down to the institute's name and the language while it is set: the site's links and Log out are one mis-tap from taking a
 * candidate off the exam, and on a phone, above all on its side, they leave no room for the question.
 *
 * Leaving the exam page does not lose anything, since every answer is saved as it is chosen and the clock is the server's, so this
 * removes the temptation and the screen space, not a danger.
 */
@Injectable({ providedIn: 'root' })
export class ExamMode {
  /** True while an open attempt is on screen. */
  readonly active = signal(false);
}
