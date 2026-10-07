import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { I18nService, LANGUAGE_STORAGE_KEY, englishTranslate, englishWords } from './i18n.service';

describe('I18nService', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.lang = '';
  });
  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  const service = () => TestBed.inject(I18nService);
  const browserLanguages = (...tags: string[]) => vi.spyOn(window.navigator, 'languages', 'get').mockReturnValue(tags);

  it('starts in English when nothing is chosen and the browser asks for something unsupported', () => {
    browserLanguages('fr-FR', 'de');

    expect(service().language()).toBe('en');
  });

  it('starts in the first language the browser lists that the interface offers', () => {
    browserLanguages('fr-FR', 'hi-IN', 'en');

    expect(service().language()).toBe('hi');
  });

  it('prefers what the user chose before to what the browser asks for', () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'mr');
    browserLanguages('hi-IN');

    expect(service().language()).toBe('mr');
  });

  it('ignores a stored language the interface does not offer', () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'fr');
    browserLanguages('en-US');

    expect(service().language()).toBe('en');
  });

  it('remembers a chosen language, and tells the page what language it is in', () => {
    const i18n = service();

    i18n.setLanguage('hi');

    expect(i18n.language()).toBe('hi');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('hi');
    expect(document.documentElement.lang).toBe('hi');
  });

  it('still switches for this visit when the browser will not store the choice', () => {
    const i18n = service();
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    i18n.setLanguage('mr');

    expect(i18n.language()).toBe('mr');
  });

  it('looks a message up in the current language, and follows a change of language', () => {
    const i18n = service();

    expect(i18n.t('nav.myExams')).toBe('My exams');
    i18n.setLanguage('hi');
    expect(i18n.t('nav.myExams')).toBe('मेरी परीक्षाएँ');
    i18n.setLanguage('mr');
    expect(i18n.t('nav.myExams')).toBe('माझ्या परीक्षा');
  });

  it('fills in the parameters of a message, and leaves one that was not given visible', () => {
    const i18n = service();

    expect(i18n.t('common.attempt', { number: 3 })).toBe('Attempt 3');
    expect(i18n.t('common.attempt')).toBe('Attempt {number}');
    expect(i18n.t('common.attempt', { other: 1 })).toBe('Attempt {number}');
  });

  it('picks the singular form for exactly one and the plural for anything else, passing the count along', () => {
    const i18n = service();

    expect(i18n.plural('marks', 1)).toBe('1 mark');
    expect(i18n.plural('marks', 2)).toBe('2 marks');
    expect(i18n.plural('marks', 0.5)).toBe('0.5 marks');
    expect(i18n.plural('time.minutes', 1)).toBe('1 minute');
  });

  it('builds plurals in the current language', () => {
    const i18n = service();
    i18n.setLanguage('hi');

    expect(i18n.plural('time.minutes', 30)).toBe('30 मिनट');
  });

  it('has an English translator that needs no service', () => {
    expect(englishTranslate('nav.logout')).toBe('Log out');
    expect(englishWords.plural('marks', 4)).toBe('4 marks');
  });
});
