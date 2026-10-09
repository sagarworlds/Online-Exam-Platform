import { Component, inject, signal } from '@angular/core';
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
    <select id="ui-language" class="language-switcher" [value]="i18n.language()" (change)="choose($event)">
      @for (language of i18n.languages; track language.code) {
        <option [value]="language.code" [selected]="language.code === i18n.language()">{{ language.label }}</option>
      }
    </select>
    @if (failed()) {
      <p class="error-message" role="alert">{{ 'common.somethingWrong' | t }}</p>
    }
  `,
})
export class LanguageSwitcher {
  protected readonly i18n = inject(I18nService);

  /** Set when the chosen language's messages could not be fetched, so the picker says so rather than doing nothing. */
  protected readonly failed = signal(false);

  protected choose(event: Event): void {
    const select = event.target as HTMLSelectElement;
    if (!isUiLanguage(select.value)) {
      return;
    }
    this.failed.set(false);
    this.i18n.setLanguage(select.value).catch((error: unknown) => {
      console.error(`The ${select.value} messages could not be loaded.`, error);
      select.value = this.i18n.language();
      this.failed.set(true);
    });
  }
}
