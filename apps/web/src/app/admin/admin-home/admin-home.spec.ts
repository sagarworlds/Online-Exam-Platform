import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthSessionService } from '../../auth/auth-session.service';
import { I18nService } from '../../i18n/i18n.service';
import { AdminHome } from './admin-home';

function render(permissions: string[]): HTMLElement {
  TestBed.configureTestingModule({
    imports: [AdminHome],
    providers: [
      provideRouter([]),
      { provide: AuthSessionService, useValue: { hasPermission: (code: string) => permissions.includes(code) } },
    ],
  });
  const fixture = TestBed.createComponent(AdminHome);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('AdminHome', () => {
  it('offers every admin area to a user holding all the permissions', () => {
    const text = render([
      'question.manage',
      'question.read',
      'exam.read',
      'invite.manage',
      'batch.manage',
      'guardian.link.manage',
      'identity.otp.read',
      'admin.whatsapp.test',
    ]).textContent;

    for (const label of ['Questions', 'Books', 'Exams', 'Invites', 'Batches', 'Guardians', 'Candidate codes', 'WhatsApp test']) {
      expect(text).toContain(label);
    }
  });

  it('offers the WhatsApp test to a user holding its permission, linking to its page', () => {
    const page = render(['admin.whatsapp.test']);

    expect(page.textContent).toContain('WhatsApp test');
    expect(page.textContent).toContain('Check that WhatsApp can send, and see exactly why not if it cannot.');
    expect(page.querySelector('a')?.getAttribute('href')).toBe('/admin/whatsapp');
  });

  it('keeps the WhatsApp test from a user without its permission, however many others they hold', () => {
    const page = render(['question.manage', 'question.read', 'exam.read', 'invite.manage', 'identity.otp.read']);

    expect(page.textContent).not.toContain('WhatsApp test');
    expect(page.querySelector('a[href="/admin/whatsapp"]')).toBeNull();
  });

  it('offers only the areas the permissions open', () => {
    const page = render(['question.read']);

    expect(page.textContent).toContain('Questions');
    expect(page.textContent).not.toContain('Invites');
    expect(page.querySelector('a')?.getAttribute('href')).toBe('/admin/questions');
  });

  it('offers the disputes queue to whoever manages exams, as it does the attempt requests', () => {
    const page = render(['exam.manage']);

    expect(page.textContent).toContain('Disputes');
    expect(page.textContent).toContain('Attempt requests');
    expect(Array.from(page.querySelectorAll('a')).map((a) => a.getAttribute('href'))).toContain('/admin/disputes');
  });

  it('keeps the disputes queue from those who may not manage exams', () => {
    expect(render(['exam.read', 'question.manage']).textContent).not.toContain('Disputes');
  });

  it('offers the reported issues to whoever manages exams', () => {
    const page = render(['exam.manage']);

    expect(page.textContent).toContain('Reported issues');
    expect(Array.from(page.querySelectorAll('a')).map((a) => a.getAttribute('href'))).toContain('/admin/issue-reports');
  });

  it('keeps the reported issues from those who may not manage exams', () => {
    expect(render(['exam.read', 'question.manage']).textContent).not.toContain('Reported issues');
  });

  it('says so when no area is open to the user', () => {
    expect(render([]).textContent).toContain('no admin areas');
  });

  it('shows each open area as a whole-row link, with the icon the sidebar uses for it', () => {
    const page = render(['question.read', 'invite.manage']);
    const rows = Array.from(page.querySelectorAll<HTMLAnchorElement>('a.admin-area'));

    expect(rows.map((a) => a.getAttribute('href'))).toEqual(['/admin/questions', '/invites']);
    expect(rows.every((a) => a.querySelector('app-admin-icon') !== null)).toBe(true);
  });

  it('reads the page and the areas in the language the admin chose', async () => {
    TestBed.configureTestingModule({
      imports: [AdminHome],
      providers: [provideRouter([]), { provide: AuthSessionService, useValue: { hasPermission: () => true } }],
    });
    const fixture = TestBed.createComponent(AdminHome);
    fixture.detectChanges();

    await TestBed.inject(I18nService).setLanguage('hi');
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('प्रशासन');
    expect(text).toContain('प्रश्न');
    localStorage.clear();
  });
});
