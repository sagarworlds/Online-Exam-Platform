import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AdminSection, SETTINGS_PAGES } from '../../auth/admin-sections';
import { I18nService } from '../../i18n/i18n.service';
import { AdminAreaList } from './admin-area-list';

function render(sections: readonly AdminSection[]) {
  TestBed.configureTestingModule({ imports: [AdminAreaList], providers: [provideRouter([])] });
  const fixture = TestBed.createComponent(AdminAreaList);
  fixture.componentRef.setInput('sections', sections);
  fixture.detectChanges();
  return { fixture, page: fixture.nativeElement as HTMLElement };
}

describe('AdminAreaList', () => {
  afterEach(() => localStorage.clear());

  it('shows each area as a whole-row link to its address, with its name, its description and its icon', () => {
    const { page } = render(SETTINGS_PAGES);
    const rows = Array.from(page.querySelectorAll<HTMLAnchorElement>('a.admin-area'));

    expect(rows.map((a) => a.getAttribute('href'))).toEqual(['/admin/branding', '/admin/instruction-templates', '/admin/whatsapp', '/admin/otp-codes']);
    expect(rows.map((a) => a.querySelector('.admin-area__name')?.textContent?.trim())).toEqual([
      'Branding',
      'Instruction templates',
      'WhatsApp test',
      'Candidate codes',
    ]);
    expect(rows[2].querySelector('.hint')?.textContent?.trim()).toBe('Check that WhatsApp can send, and see exactly why not if it cannot.');
    expect(rows.every((a) => a.querySelector('app-admin-icon svg') !== null)).toBe(true);
  });

  it('is a list, one item to an area, so a screen reader counts them', () => {
    const { page } = render(SETTINGS_PAGES);

    expect(page.querySelectorAll('ul.admin-areas > li').length).toBe(SETTINGS_PAGES.length);
  });

  it('lists nothing for no areas', () => {
    const { page } = render([]);

    expect(page.querySelectorAll('a')).toHaveLength(0);
  });

  it('reads the names and descriptions in the language the admin chose', async () => {
    const { fixture, page } = render(SETTINGS_PAGES.slice(0, 1));

    await TestBed.inject(I18nService).setLanguage('hi');
    fixture.detectChanges();

    expect(page.querySelector('.admin-area__name')?.textContent?.trim()).toBe('ब्रांडिंग');
  });
});
