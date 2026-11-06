import { TestBed } from '@angular/core/testing';
import { I18nService } from '../i18n/i18n.service';
import { THEME_STORAGE_KEY } from './theme.service';
import { ThemeSwitcher } from './theme-switcher';

describe('ThemeSwitcher', () => {
  afterEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
  });

  function open() {
    const fixture = TestBed.createComponent(ThemeSwitcher);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    return { fixture, root, select: () => root.querySelector<HTMLSelectElement>('select')! };
  }

  it('offers System, Light and Dark, labelled for a screen reader, on System at first', () => {
    const { root, select } = open();

    expect(root.querySelector('label')?.textContent).toBe('Theme');
    expect(root.querySelector('label')?.getAttribute('for')).toBe(select().id);
    expect(Array.from(select().options).map((option) => option.textContent?.trim())).toEqual(['System theme', 'Light', 'Dark']);
    expect(select().value).toBe('system');
  });

  it('changes the theme of the page when one is picked', () => {
    const { select } = open();

    select().value = 'dark';
    select().dispatchEvent(new Event('change'));

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');
  });

  it('shows the theme kept from before', () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'light');

    expect(open().select().value).toBe('light');
  });

  it('is worded in the language chosen', async () => {
    await TestBed.inject(I18nService).setLanguage('hi');
    const { root } = open();

    expect(root.querySelector('label')?.textContent).toBe('थीम');
    expect(root.querySelector('select')?.textContent).toContain('गहरी');
    localStorage.clear();
  });
});
