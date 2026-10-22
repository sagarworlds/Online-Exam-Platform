import { formatDate } from '@angular/common';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { I18nService } from './i18n.service';
import { LocaleDatePipe } from './locale-date.pipe';

@Component({ imports: [LocaleDatePipe], template: `<p>{{ moment | date: 'mediumDate' }}</p>` })
class Host {
  moment = new Date(2026, 9, 22, 11, 30);
}

describe('LocaleDatePipe', () => {
  const moment = new Date(2026, 9, 22, 11, 30);

  afterEach(() => localStorage.clear());

  const pipe = () => TestBed.runInInjectionContext(() => new LocaleDatePipe());

  it('writes a date in English exactly as Angular does, so the screens that were in English read as they did', () => {
    expect(pipe().transform(moment, 'medium')).toBe(formatDate(moment, 'medium', 'en-US'));
  });

  it('writes the same moment in Hindi and in Marathi once that language is chosen', async () => {
    const i18n = TestBed.inject(I18nService);
    const english = pipe().transform(moment, 'mediumDate');

    await i18n.setLanguage('hi');
    const hindi = pipe().transform(moment, 'mediumDate');
    await i18n.setLanguage('mr');
    const marathi = pipe().transform(moment, 'mediumDate');

    expect(english).toBe('Oct 22, 2026');
    expect(hindi).not.toBe(english);
    expect(hindi).toContain('2026');
    expect(marathi).not.toBe(english);
    expect(marathi).toContain('2026');
  });

  it('says nothing for a missing date, as the date pipe of Angular does', () => {
    expect(pipe().transform(null)).toBeNull();
    expect(pipe().transform(undefined)).toBeNull();
    expect(pipe().transform('')).toBeNull();
  });

  it('redraws a date already on screen when the language changes', async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toBe('Oct 22, 2026');

    await TestBed.inject(I18nService).setLanguage('hi');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toBe('Oct 22, 2026');
  });
});
