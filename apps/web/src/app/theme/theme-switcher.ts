import { Component, inject } from '@angular/core';
import { MessageKey } from '../i18n/messages.en';
import { TranslatePipe } from '../i18n/translate.pipe';
import { THEME_CHOICES, ThemeChoice, ThemeService, isThemeChoice } from './theme.service';

const LABELS: Record<ThemeChoice, MessageKey> = { system: 'theme.system', light: 'theme.light', dark: 'theme.dark' };

/** The theme picker in the page header, beside the language picker: System, Light or Dark. A native select, so the keyboard and the screen reader work as for any other. */
@Component({
  selector: 'app-theme-switcher',
  imports: [TranslatePipe],
  styleUrl: './theme-switcher.css',
  template: `
    <label class="visually-hidden" for="ui-theme">{{ 'theme.label' | t }}</label>
    <select id="ui-theme" class="theme-switcher" [value]="theme.choice()" (change)="choose($event)">
      @for (choice of choices; track choice) {
        <option [value]="choice" [selected]="choice === theme.choice()">{{ labels[choice] | t }}</option>
      }
    </select>
  `,
})
export class ThemeSwitcher {
  protected readonly theme = inject(ThemeService);
  protected readonly choices = THEME_CHOICES;
  protected readonly labels = LABELS;

  protected choose(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    if (isThemeChoice(value)) {
      this.theme.set(value);
    }
  }
}
