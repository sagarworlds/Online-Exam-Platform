import { Pipe, PipeTransform, inject } from '@angular/core';
import { I18nService, MessageParams } from './i18n.service';
import { MessageKey } from './messages.en';

/**
 * `{{ 'nav.myExams' | t }}` or `{{ 'attempt.answeredOf' | t: { answered: 3, total: 10 } }}`: a message in the current language (FR-51).
 *
 * Impure on purpose. A pure pipe is only re-run when its inputs change, and its input here is a constant key, so it would keep showing
 * the language it first ran in; running every pass is what lets it follow {@link I18nService.language}. It does a map lookup, so the cost is nil.
 */
@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(key: MessageKey, params?: MessageParams): string {
    return this.i18n.t(key, params);
  }
}
