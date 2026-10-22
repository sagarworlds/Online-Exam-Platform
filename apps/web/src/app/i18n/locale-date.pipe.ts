import { formatDate } from '@angular/common';
import { Pipe, PipeTransform, inject } from '@angular/core';
import { I18nService } from './i18n.service';

/**
 * `{{ time | date: 'medium' }}`: a date written the way the language on screen writes it (FR-51), so a Hindi or Marathi reader gets their
 * own month names and order, not English ones. It stands in for Angular's `DatePipe`, which is fixed to one locale for the whole app, and
 * takes the same formats and arguments.
 *
 * Impure on purpose, like the translate pipe: a pure pipe is only run again when its input changes, which would keep a date in the language
 * it was first written in after the reader switches. It formats a date, so the cost is small.
 */
@Pipe({ name: 'date', pure: false })
export class LocaleDatePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(value: Date | string | number | null | undefined, format = 'mediumDate', timezone?: string): string | null {
    if (value === null || value === undefined || value === '') {
      return null;
    }

    return formatDate(value, format, this.i18n.locale(), timezone);
  }
}
