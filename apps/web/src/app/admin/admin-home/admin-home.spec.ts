import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthSessionService } from '../../auth/auth-session.service';
import { I18nService } from '../../i18n/i18n.service';
import { AdminHome } from './admin-home';

const holding = (permissions: string[]) => ({
  hasAnyPermission: (codes: readonly string[]) => codes.some((code) => permissions.includes(code)),
});

function render(permissions: string[]): HTMLElement {
  TestBed.configureTestingModule({
    imports: [AdminHome],
    providers: [provideRouter([]), { provide: AuthSessionService, useValue: holding(permissions) }],
  });
  const fixture = TestBed.createComponent(AdminHome);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

/** The names of the areas listed, in the order shown. */
const areaNames = (page: HTMLElement) => Array.from(page.querySelectorAll('.admin-area__name')).map((name) => name.textContent?.trim());
const headings = (page: HTMLElement) => Array.from(page.querySelectorAll('h2')).map((heading) => heading.textContent?.trim());
const hrefs = (page: HTMLElement) => Array.from(page.querySelectorAll('a')).map((a) => a.getAttribute('href'));

describe('AdminHome', () => {
  it('offers every admin area to a user holding all the permissions, with Settings in place of the pages it gathers', () => {
    const page = render([
      'question.manage',
      'question.read',
      'exam.read',
      'exam.manage',
      'student.read',
      'batch.manage',
      'guardian.link.manage',
      'identity.otp.read',
      'admin.whatsapp.test',
    ]);

    expect(areaNames(page)).toEqual([
      'Questions',
      'Books',
      'Exams',
      'Students',
      'Batches',
      'Guardians',
      'Attempt requests',
      'Disputes',
      'Reported issues',
      'Settings',
    ]);
  });

  it('does not list the pages gathered under Settings, even to a user who may open all of them', () => {
    const page = render(['exam.manage', 'admin.whatsapp.test', 'identity.otp.read']);

    for (const name of ['Branding', 'Instruction templates', 'WhatsApp test', 'Candidate codes']) {
      expect(areaNames(page)).not.toContain(name);
    }
    for (const path of ['/admin/branding', '/admin/instruction-templates', '/admin/whatsapp', '/admin/otp-codes']) {
      expect(hrefs(page)).not.toContain(path);
    }
  });

  it('offers Settings to a user who may open just one of its pages, linking to the Settings page', () => {
    const page = render(['admin.whatsapp.test']);

    expect(areaNames(page)).toEqual(['Settings']);
    expect(page.textContent).toContain('Branding, instruction templates, the WhatsApp test and candidate codes.');
    expect(page.querySelector('a')?.getAttribute('href')).toBe('/admin/settings');
  });

  it.each(['exam.manage', 'identity.otp.read', 'admin.whatsapp.test'])('offers Settings to a user whose only permission is %s', (permission) => {
    expect(areaNames(render([permission]))).toContain('Settings');
  });

  it('keeps Settings from a user who may open none of its pages, however many other areas they may open', () => {
    const page = render(['question.manage', 'question.read', 'exam.read', 'student.read', 'batch.manage']);

    expect(areaNames(page)).not.toContain('Settings');
    expect(page.querySelector('a[href="/admin/settings"]')).toBeNull();
  });

  it('offers only the areas the permissions open', () => {
    const page = render(['question.read']);

    expect(areaNames(page)).toEqual(['Questions']);
    expect(page.textContent).not.toContain('Students');
    expect(page.querySelector('a')?.getAttribute('href')).toBe('/admin/questions');
  });

  it('offers the disputes queue to whoever manages exams, as it does the attempt requests', () => {
    const page = render(['exam.manage']);

    expect(page.textContent).toContain('Disputes');
    expect(page.textContent).toContain('Attempt requests');
    expect(hrefs(page)).toContain('/admin/disputes');
  });

  it('keeps the disputes queue from those who may not manage exams', () => {
    expect(render(['exam.read', 'question.manage']).textContent).not.toContain('Disputes');
  });

  it('offers the reported issues to whoever manages exams', () => {
    const page = render(['exam.manage']);

    expect(page.textContent).toContain('Reported issues');
    expect(hrefs(page)).toContain('/admin/issue-reports');
  });

  it('keeps the reported issues from those who may not manage exams', () => {
    expect(render(['exam.read', 'question.manage']).textContent).not.toContain('Reported issues');
  });

  it('says so when no area is open to the user', () => {
    const page = render([]);

    expect(page.textContent).toContain('no admin areas');
    expect(headings(page)).toEqual([]);
  });

  it('shows each open area as a whole-row link, with the icon the sidebar uses for it', () => {
    const page = render(['question.read', 'student.read']);
    const rows = Array.from(page.querySelectorAll<HTMLAnchorElement>('a.admin-area'));

    expect(rows.map((a) => a.getAttribute('href'))).toEqual(['/admin/questions', '/students']);
    expect(rows.every((a) => a.querySelector('app-admin-icon') !== null)).toBe(true);
  });

  describe('groups', () => {
    it('puts the areas under the headings the sidebar uses, in its order', () => {
      const page = render(['question.read', 'exam.read', 'student.read', 'exam.manage']);
      const groups = Array.from(page.querySelectorAll('.admin-group')).map((group) => ({
        heading: group.querySelector('h2')?.textContent?.trim() ?? null,
        areas: Array.from(group.querySelectorAll('.admin-area__name')).map((name) => name.textContent?.trim()),
      }));

      expect(groups).toEqual([
        { heading: 'Content', areas: ['Questions'] },
        { heading: 'Exams', areas: ['Exams'] },
        { heading: 'People', areas: ['Students'] },
        { heading: 'Reviews', areas: ['Attempt requests', 'Disputes', 'Reported issues'] },
        { heading: null, areas: ['Settings'] },
      ]);
    });

    it('draws no heading over a group in which the user may open nothing', () => {
      const page = render(['question.read']);

      expect(headings(page)).toEqual(['Content']);
      expect(page.querySelectorAll('.admin-group').length).toBe(1);
    });

    it('draws no empty heading for a user who may open only Settings', () => {
      const page = render(['identity.otp.read']);

      expect(headings(page)).toEqual([]);
      expect(areaNames(page)).toEqual(['Settings']);
    });

    it('makes each heading a real heading below the page title', () => {
      const page = render(['question.read', 'student.read']);

      expect(page.querySelectorAll('h1').length).toBe(1);
      expect(headings(page)).toEqual(['Content', 'People']);
    });
  });

  it('reads the page and the areas in the language the admin chose', async () => {
    TestBed.configureTestingModule({
      imports: [AdminHome],
      providers: [provideRouter([]), { provide: AuthSessionService, useValue: { hasAnyPermission: () => true } }],
    });
    const fixture = TestBed.createComponent(AdminHome);
    fixture.detectChanges();

    await TestBed.inject(I18nService).setLanguage('hi');
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('प्रशासन');
    expect(text).toContain('प्रश्न');
    expect(text).toContain('सामग्री');
    expect(text).toContain('सेटिंग');
    localStorage.clear();
  });
});
