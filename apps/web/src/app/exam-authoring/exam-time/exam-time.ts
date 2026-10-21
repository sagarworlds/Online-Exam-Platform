import { Pipe, PipeTransform, inject } from '@angular/core';
import { I18nService } from '../../i18n/i18n.service';
import { isKnownTimeZone } from '../exam-scheduler/zoned-time';

/**
 * A moment as an exam shows it: the date and the clock time as they are in the exam's own time zone, with the zone's name ("10:00 am IST").
 * The scheduler reads and writes in that zone, so every screen that shows when the exam opens must show it in the same one: an author in
 * another zone would otherwise see 10:00 where they typed it and another time on the editor, for the same instant. A zone the browser does not
 * know falls back to the browser's own, which still names itself.
 */
export function formatInExamZone(utc: string | null | undefined, timeZone: string | null | undefined, locale?: string): string {
  if (!utc) {
    return '';
  }

  const zone = timeZone && isKnownTimeZone(timeZone) ? timeZone : undefined;
  // The parts are named one by one: `dateStyle` and `timeStyle` cannot be combined with the zone's name.
  return new Intl.DateTimeFormat(locale, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
    timeZone: zone,
    timeZoneName: 'short',
  }).format(new Date(utc));
}

/**
 * `{{ exam.scheduledStartTime | examTime: exam.timeZone }}`. Impure on purpose, like the translate pipe: it follows the language on screen,
 * which a pure pipe with constant inputs would not.
 */
@Pipe({ name: 'examTime', pure: false })
export class ExamTimePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(utc: string | null | undefined, timeZone: string | null | undefined): string {
    return formatInExamZone(utc, timeZone, this.i18n.language());
  }
}
