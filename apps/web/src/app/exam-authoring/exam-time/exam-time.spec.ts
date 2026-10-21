import { TestBed } from '@angular/core/testing';
import { I18nService } from '../../i18n/i18n.service';
import { ExamTimePipe, formatInExamZone } from './exam-time';

describe('exam times', () => {
  it('shows the clock time of the exam’s own zone, and names the zone, whatever zone the browser is in', () => {
    const start = '2026-10-05T04:30:00Z';

    expect(formatInExamZone(start, 'Asia/Kolkata', 'en')).toMatch(/Oct 5, 2026.*10:00\s?AM.*(IST|GMT\+5:30)/);
    expect(formatInExamZone(start, 'America/New_York', 'en')).toMatch(/Oct 5, 2026.*12:30\s?AM.*(EDT|GMT-4)/);
    expect(formatInExamZone(start, 'UTC', 'en')).toMatch(/Oct 5, 2026.*4:30\s?AM.*UTC/);
  });

  it('is the same moment in two zones, which is the point: the scheduler and the editor agree', () => {
    const india = formatInExamZone('2026-10-05T04:30:00Z', 'Asia/Kolkata', 'en');
    const utc = formatInExamZone('2026-10-05T04:30:00Z', 'UTC', 'en');

    expect(india).not.toBe(utc);
  });

  it('is empty for no moment, and falls back to the browser’s zone for one it does not know', () => {
    expect(formatInExamZone(null, 'Asia/Kolkata')).toBe('');
    expect(formatInExamZone('', 'Asia/Kolkata')).toBe('');
    expect(formatInExamZone('2026-10-05T04:30:00Z', 'Mars/Olympus_Mons', 'en')).toContain('2026');
    expect(formatInExamZone('2026-10-05T04:30:00Z', undefined, 'en')).toContain('2026');
  });

  it('follows the language on screen', async () => {
    const pipe = TestBed.runInInjectionContext(() => new ExamTimePipe());
    const english = pipe.transform('2026-10-05T04:30:00Z', 'Asia/Kolkata');

    await TestBed.inject(I18nService).setLanguage('hi');
    const hindi = pipe.transform('2026-10-05T04:30:00Z', 'Asia/Kolkata');

    expect(hindi).not.toBe(english);
    localStorage.clear();
  });
});
