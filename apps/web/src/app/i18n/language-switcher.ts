import { Component, inject } from '@angular/core';
import { I18nService } from './i18n.service';
import { isUiLanguage } from './languages';
import { TranslatePipe } from './translate.pipe';

/**
 * The language picker in the page header (FR-51). Each language is named in its own script, so a reader who cannot read the current one
 * can still find theirs; the label is translated, and is only for screen readers.
 */
@Component({
  selector: 'app-language-switcher',
  imports: [TranslatePipe],
  styleUrl: './language-switcher.css',
  template: `
    <label class="visually-hidden" for="ui-language">{{ 'language.label' | t }}</label>
    <select id="ui-language" class="language-switcher" [value]="i18n.language()" (change)="choose($any($event.target).value)">
      @for (language of i18n.languages; track language.code) {
        <option [value]="language.code" [selected]="language.code === i18n.language()">{{ language.label }}</option>
      }
    </select>
  `,
})
export class LanguageSwitcher {
  protected readonly i18n = inject(I18nService);

  protected choose(value: string): void {
    if (isUiLanguage(value)) {
      this.i18n.setLanguage(value);
    }
  }
}
