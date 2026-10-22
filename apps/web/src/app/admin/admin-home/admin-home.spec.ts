import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthSessionService } from '../../auth/auth-session.service';
import { I18nService } from '../../i18n/i18n.service';
import { AdminHome } from './admin-home';

const holding = (permissions: string[]) => ({
  hasPermission: (code: string) => permissions.includes(code),
  hasAnyPermission: (codes: readonly string[]) => codes.some((code) => permissions.includes(code)),
});

function open(permissions: string[]) {
  TestBed.configureTestingModule({
    imports: [AdminHome],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: AuthSessionService, useValue: holding(permissions) }],
  });
  const fixture = TestBed.createComponent(AdminHome);
  fixture.detectChanges();
  return { fixture, page: fixture.nativeElement as HTMLElement, http: TestBed.inject(HttpTestingController) };
}

function render(permissions: string[]): HTMLElement {
  return open(permissions).page;
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

  describe('what needs attention', () => {
    const isRead = (path: string) => (r: { method: string; url: string }) => r.method === 'GET' && r.url.includes(path);
    const tile = (page: HTMLElement, name: string) =>
      Array.from(page.querySelectorAll<HTMLAnchorElement>('a.attention-tile')).find((a) => a.getAttribute('aria-label')?.startsWith(name));
    const hour = 3_600_000;
    const exam = (id: string, over: Record<string, unknown> = {}) => ({
      id,
      name: id,
      status: 'Published',
      isScheduled: true,
      scheduledStartTime: new Date(Date.now() - hour).toISOString(),
      scheduledEndTime: new Date(Date.now() + hour).toISOString(),
      ...over,
    });

    /** Answers the three reads the page makes for staff who may manage exams. */
    function answerQueues(http: HttpTestingController, answers: { requests?: unknown[]; disputes?: unknown[]; issues?: unknown[] } = {}) {
      http.expectOne(isRead('/v1/attempt-requests')).flush(answers.requests ?? []);
      http.expectOne(isRead('/v1/disputes')).flush(answers.disputes ?? []);
      http.expectOne(isRead('/v1/issue-reports')).flush(answers.issues ?? []);
    }

    it('shows a tile for each queue the user may read, checking while the counts are on their way', () => {
      const { page } = open(['exam.manage', 'exam.read']);

      expect(page.querySelector('h2#admin-attention-heading')?.textContent).toContain('Needs attention');
      expect(Array.from(page.querySelectorAll('a.attention-tile')).map((a) => a.getAttribute('aria-label'))).toEqual([
        'Attempt requests: Checking…',
        'Disputes: Checking…',
        'Reported issues: Checking…',
        'Exams running: Checking…',
      ]);
    });

    it('counts what waits in each queue, in words, and links each tile to the page that deals with it', () => {
      const { fixture, page, http } = open(['exam.manage', 'exam.read']);

      answerQueues(http, { requests: [{}, {}, {}], disputes: [{}], issues: [] });
      http.expectOne(isRead('/v1/exams')).flush([exam('e1'), exam('e2')]);
      fixture.detectChanges();

      expect(tile(page, 'Attempt requests')?.getAttribute('aria-label')).toBe('Attempt requests: 3 waiting for a decision');
      expect(tile(page, 'Disputes')?.getAttribute('aria-label')).toBe('Disputes: 1 waiting for an answer');
      expect(tile(page, 'Reported issues')?.getAttribute('aria-label')).toBe('Reported issues: Nothing waiting');
      expect(tile(page, 'Exams running')?.getAttribute('aria-label')).toBe('Exams running: 2 open to candidates now');
      expect(tile(page, 'Attempt requests')?.getAttribute('href')).toBe('/admin/attempt-requests');
      expect(tile(page, 'Disputes')?.getAttribute('href')).toBe('/admin/disputes');
      expect(tile(page, 'Reported issues')?.getAttribute('href')).toBe('/admin/issue-reports');
      expect(tile(page, 'Exams running')?.getAttribute('href')).toBe('/exams');
    });

    it('tints a tile with something waiting, and leaves a clear one plain', () => {
      const { fixture, page, http } = open(['exam.manage']);

      answerQueues(http, { requests: [{}] });
      fixture.detectChanges();

      expect(tile(page, 'Attempt requests')?.classList.contains('attention-tile--waiting')).toBe(true);
      expect(tile(page, 'Disputes')?.classList.contains('attention-tile--waiting')).toBe(false);
    });

    it('counts only the exams that are published and open now, not a draft, an archived one, or one not yet started', () => {
      const { fixture, page, http } = open(['exam.read']);

      http.expectOne(isRead('/v1/exams')).flush([
        exam('running'),
        exam('draft', { status: 'Draft' }),
        exam('archived', { status: 'Archived' }),
        exam('unscheduled', { isScheduled: false }),
        exam('later', { scheduledStartTime: new Date(Date.now() + hour).toISOString(), scheduledEndTime: new Date(Date.now() + 2 * hour).toISOString() }),
        exam('over', { scheduledStartTime: new Date(Date.now() - 2 * hour).toISOString(), scheduledEndTime: new Date(Date.now() - hour).toISOString() }),
      ]);
      fixture.detectChanges();

      expect(tile(page, 'Exams running')?.getAttribute('aria-label')).toBe('Exams running: 1 open to candidates now');
    });

    it('says "200+" when a queue reached the most its list holds', () => {
      const { fixture, page, http } = open(['exam.manage']);

      answerQueues(http, { requests: Array.from({ length: 200 }, () => ({})) });
      fixture.detectChanges();

      expect(tile(page, 'Attempt requests')?.getAttribute('aria-label')).toContain('200+ waiting');
    });

    it('says one queue could not be checked, and still shows the others', () => {
      const { fixture, page, http } = open(['exam.manage']);

      http.expectOne(isRead('/v1/attempt-requests')).flush({}, { status: 500, statusText: 'Server Error' });
      http.expectOne(isRead('/v1/disputes')).flush([{}]);
      http.expectOne(isRead('/v1/issue-reports')).flush([]);
      fixture.detectChanges();

      expect(tile(page, 'Attempt requests')?.getAttribute('aria-label')).toBe('Attempt requests: Could not be checked');
      expect(tile(page, 'Disputes')?.getAttribute('aria-label')).toContain('1 waiting');
    });

    it('asks only for the queues the user may read', () => {
      const staff = open(['exam.read']);

      staff.http.expectOne(isRead('/v1/exams')).flush([]);
      staff.http.expectNone(isRead('/v1/disputes'));
      expect(staff.page.querySelectorAll('a.attention-tile')).toHaveLength(1);
    });

    it('shows no section at all to a user who has no queue to see', () => {
      const { page, http } = open(['question.read']);

      http.expectNone(() => true);
      expect(page.querySelector('.admin-attention')).toBeNull();
    });

    describe('the latest items under a tile', () => {
      const request = (id: string, email: string | null, examName: string | null) => ({ id, candidateEmail: email, examName });
      const toggle = (page: HTMLElement, name: string) => page.querySelector<HTMLButtonElement>(`button.attention-toggle[aria-label*="${name}"]`)!;
      const lines = (page: HTMLElement) => Array.from(page.querySelectorAll('.attention-line__title')).map((t) => t.textContent?.trim());

      it('opens from a button that says whether it is open, and lists who is waiting, each with a Review button that names them', () => {
        const { fixture, page, http } = open(['exam.manage']);
        answerQueues(http, { requests: [request('r1', 'asha@example.com', 'Maths Final'), request('r2', 'bob@example.com', 'Physics')] });
        fixture.detectChanges();

        const button = toggle(page, 'Attempt requests');
        expect(button.getAttribute('aria-expanded')).toBe('false');
        expect(page.querySelector<HTMLElement>('#attention-items-attemptRequests')!.hidden).toBe(true);

        button.click();
        fixture.detectChanges();

        expect(button.getAttribute('aria-expanded')).toBe('true');
        expect(page.querySelector<HTMLElement>('#attention-items-attemptRequests')!.hidden).toBe(false);
        expect(lines(page)).toEqual(['asha@example.com', 'bob@example.com']);
        const review = page.querySelector<HTMLAnchorElement>('#attention-items-attemptRequests a')!;
        expect(review.getAttribute('aria-label')).toBe('Review asha@example.com');
        expect(review.getAttribute('href')).toBe('/admin/attempt-requests');
        expect(page.querySelector('#attention-items-attemptRequests .attention-line__subtitle')?.textContent).toBe('Maths Final');
      });

      it('has no button for a queue with nothing waiting', () => {
        const { fixture, page, http } = open(['exam.manage']);
        answerQueues(http, { requests: [request('r1', 'asha@example.com', null)] });
        fixture.detectChanges();

        expect(toggle(page, 'Attempt requests')).not.toBeNull();
        expect(toggle(page, 'Disputes')).toBeNull();
      });

      it('shows a running exam with an Open button to the exam, not a Review', () => {
        const { fixture, page, http } = open(['exam.read']);
        http.expectOne(isRead('/v1/exams')).flush([exam('e1', { name: 'Maths Final' })]);
        fixture.detectChanges();

        toggle(page, 'Exams running').click();
        fixture.detectChanges();

        const link = page.querySelector<HTMLAnchorElement>('#attention-items-liveExams a')!;
        expect(link.textContent?.trim()).toBe('Open');
        expect(link.getAttribute('href')).toBe('/exams/e1');
        expect(link.getAttribute('aria-label')).toBe('Open Maths Final');
      });

      it('counts open risk flags across every exam, for a reviewer, and takes each line to its exam’s flags', () => {
        const { fixture, page, http } = open(['proctoring.review']);
        http.expectOne(isRead('/v1/proctoring/risk-flags/open')).flush({
          total: 7,
          items: [{ id: 'f1', examId: 'e9', examName: 'Board Mock', attemptNumber: 1, score: 60, maxScore: 100, computedAtUtc: '2026-10-01T05:00:00Z' }],
        });
        fixture.detectChanges();

        expect(tile(page, 'Risk flags')?.getAttribute('aria-label')).toBe('Risk flags: 7 open for review');
        toggle(page, 'Risk flags').click();
        fixture.detectChanges();
        expect(lines(page)).toEqual(['Board Mock']);
        expect(page.querySelector('#attention-items-riskFlags .attention-line__subtitle')?.textContent).toBe('60 / 100');
        expect(page.querySelector('#attention-items-riskFlags a')?.getAttribute('href')).toBe('/exams/e9/risk-flags');
      });

      it('shows no risk-flag tile, and asks nothing, to staff who may not review', () => {
        const { page } = open(['exam.manage']);

        expect(tile(page, 'Risk flags')).toBeUndefined();
      });
    });

    it('reads the counts again from the refresh icon, keeping what was shown until the new numbers arrive', () => {
      const { fixture, page, http } = open(['exam.manage']);
      answerQueues(http);
      fixture.detectChanges();

      page.querySelector<HTMLButtonElement>('.admin-attention app-refresh-button button')!.click();
      fixture.detectChanges();
      expect(tile(page, 'Attempt requests')?.getAttribute('aria-label')).toBe('Attempt requests: Nothing waiting');

      answerQueues(http, { requests: [{}, {}] });
      fixture.detectChanges();
      expect(tile(page, 'Attempt requests')?.getAttribute('aria-label')).toBe('Attempt requests: 2 waiting for a decision');
    });
  });

  it('reads the page and the areas in the language the admin chose', async () => {
    TestBed.configureTestingModule({
      imports: [AdminHome],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthSessionService, useValue: { hasPermission: () => true, hasAnyPermission: () => true } },
      ],
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
