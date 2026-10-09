import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { I18nService, LANGUAGE_STORAGE_KEY } from './i18n.service';
import { LanguageSwitcher } from './language-switcher';

describe('LanguageSwitcher', () => {
  let fixture: ComponentFixture<LanguageSwitcher>;
  let root: HTMLElement;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({ imports: [LanguageSwitcher] }).compileComponents();
    fixture = TestBed.createComponent(LanguageSwitcher);
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => localStorage.clear());

  const select = () => root.querySelector('select') as HTMLSelectElement;

  it('offers each language in its own script, English first', () => {
    expect(Array.from(select().options).map((o) => [o.value, o.textContent?.trim()])).toEqual([
      ['en', 'English'],
      ['hi', 'हिन्दी'],
      ['mr', 'मराठी'],
    ]);
    expect(select().value).toBe('en');
  });

  afterEach(() => vi.restoreAllMocks());

  it('says so, and keeps the picker on the language in use, when the chosen messages cannot be fetched', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    vi.spyOn(TestBed.inject(I18nService), 'setLanguage').mockRejectedValue(new Error('offline'));

    select().value = 'mr';
    select().dispatchEvent(new Event('change'));

    await vi.waitFor(() => expect(root.querySelector('[role="alert"]')).not.toBeNull());
    expect(select().value).toBe('en');
    expect(root.querySelector('[role="alert"]')?.textContent?.trim()).toBe('Something went wrong. Please try again.');
  });

  it('is labelled for screen readers in the current language', async () => {
    expect(root.querySelector('label')?.textContent?.trim()).toBe('Language');

    await TestBed.inject(I18nService).setLanguage('hi');
    fixture.detectChanges();

    expect(root.querySelector('label')?.textContent?.trim()).toBe('भाषा');
  });

  it('switches the interface and remembers the choice', async () => {
    select().value = 'mr';
    select().dispatchEvent(new Event('change'));
    await vi.waitFor(() => expect(TestBed.inject(I18nService).language()).toBe('mr'));
    fixture.detectChanges();

    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('mr');
    expect(root.querySelector('label')?.textContent?.trim()).toBe('भाषा');
  });

  it('ignores a value that is not a language it offers', () => {
    const i18n = TestBed.inject(I18nService);
    const option = document.createElement('option');
    option.value = 'fr';
    select().appendChild(option);
    select().value = 'fr';
    select().dispatchEvent(new Event('change'));

    expect(i18n.language()).toBe('en');
  });
});
