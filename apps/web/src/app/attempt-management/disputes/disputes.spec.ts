import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthSessionService } from '../../auth/auth-session.service';
import { LANGUAGE_STORAGE_KEY } from '../../i18n/i18n.service';
import { HI } from '../../i18n/messages.hi';
import { MR } from '../../i18n/messages.mr';
import { QuestionDto } from '../../question-bank/question.models';
import { DisputeRow } from '../attempt-admin.models';
import { Disputes } from './disputes';

const row = (id: string, overrides: Partial<DisputeRow> = {}): DisputeRow => ({
  id,
  examId: 'e1',
  examName: 'Maths Final',
  attemptId: `a-${id}`,
  attemptNumber: 1,
  candidateId: `c-${id}`,
  candidateEmail: `${id}@example.com`,
  questionId: 'q1',
  questionText: '<p>Capital of France?</p>',
  reason: 'Paris is the capital',
  raisedAtUtc: '2026-10-05T05:00:00Z',
  status: 'Open',
  resolvedAtUtc: null,
  resolutionNote: null,
  ...overrides,
});

const question = (id = 'q1'): QuestionDto => ({
  id,
  text: '<p>Capital of France?</p>',
  options: [
    { id: 'o1', text: 'Rome', isCorrect: true, isPinned: false },
    { id: 'o2', text: 'Paris', isCorrect: false, isPinned: false },
  ],
  createdBy: 'author',
  createdAtUtc: '2026-10-01T09:00:00Z',
  chapterId: null,
  chapterTitle: null,
  bookId: null,
  bookName: null,
  classId: null,
  className: null,
  usage: { examCount: 1, examNames: ['Maths Final'], answered: true },
  difficulty: null,
  topics: [],
  allowsMultiple: false,
});

const MANAGE_EXAMS = 'exam.manage';
const MANAGE_QUESTIONS = 'question.manage';

describe('Disputes', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<Disputes>;
  let root: HTMLElement;

  const isList = (r: { method: string; url: string }) =>
    r.method === 'GET' && r.url.includes('/v1/disputes');
  const isReject = (id: string) => (r: { method: string; url: string }) =>
    r.method === 'POST' && r.url.endsWith(`/v1/disputes/${id}/reject`);
  const isQuestionGet = (id: string) => (r: { method: string; url: string }) =>
    r.method === 'GET' && r.url.endsWith(`/v1/questions/${id}`);
  const isCorrection = (id: string) => (r: { method: string; url: string }) =>
    r.method === 'POST' && r.url.endsWith(`/v1/questions/${id}/correct-answer-key`);

  async function open(
    rows: DisputeRow[] | 'error',
    permissions: string[] = [MANAGE_EXAMS, MANAGE_QUESTIONS],
  ) {
    await TestBed.configureTestingModule({
      imports: [Disputes],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: AuthSessionService,
          useValue: { hasPermission: (code: string) => permissions.includes(code) },
        },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);

    fixture = TestBed.createComponent(Disputes);
    fixture.detectChanges();
    const list = httpMock.expectOne(isList);
    expect(list.request.params.get('status')).toBe('open');
    if (rows === 'error') {
      list.flush(
        { title: 'forbidden', detail: 'No access.' },
        { status: 403, statusText: 'Forbidden' },
      );
    } else {
      list.flush(rows);
    }
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  const text = () => (root.textContent ?? '').replace(/\s+/g, ' ');
  const line = (element: Element | null | undefined) =>
    (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const buttons = (label: string) =>
    Array.from(root.querySelectorAll('button')).filter((b) => b.textContent?.trim() === label);
  /** The Correct buttons of the groups: each names how many open disputes its correction accepts. */
  const correctButtons = () =>
    Array.from(root.querySelectorAll('button')).filter((b) =>
      /^Correct the answer key — accepts/.test(b.textContent?.trim() ?? ''),
    );
  const press = (label: string, index = 0) => {
    buttons(label)[index].click();
    fixture.detectChanges();
  };
  const pressCorrect = (index = 0) => {
    correctButtons()[index].click();
    fixture.detectChanges();
  };
  const groups = () => Array.from(root.querySelectorAll<HTMLElement>('section.card'));
  const rows = (group: HTMLElement) =>
    Array.from(group.querySelectorAll<HTMLElement>('.dispute-row'));
  const results = () => Array.from(root.querySelectorAll<HTMLElement>('.dispute-result'));
  const type = (box: HTMLTextAreaElement, value: string) => {
    box.value = value;
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const choose = (value: string) => {
    const select = root.querySelector('select') as HTMLSelectElement;
    select.value = value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  };
  /** Lets the page draw what the last action changed, and any focus that waits for it, to be moved. */
  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  describe('the list', () => {
    it('shows the open disputes with who raised each, for which exam and attempt, when, and why', async () => {
      await open([row('amy', { raisedAtUtc: '2026-10-05T05:00:00Z', attemptNumber: 2 })]);

      const content = text();
      expect(content).toContain('amy@example.com');
      expect(content).toContain('Maths Final');
      expect(content).toContain('attempt 2');
      expect(content).toContain('raised Oct');
      expect(content).toContain('Paris is the capital');
      expect(root.querySelector('.rich-text')?.textContent).toContain('Capital of France?');
      expect(root.querySelector('a')?.getAttribute('href')).toBe('/exams/e1/attempts');
    });

    it('says so when the candidate, exam, question or attempt number can no longer be read', async () => {
      await open([
        row('amy', {
          candidateEmail: null,
          examName: null,
          questionText: null,
          attemptNumber: null,
        }),
      ]);

      expect(text()).toContain('A candidate who is no longer enrolled');
      expect(text()).toContain('An exam that can no longer be read');
      expect(text()).toContain('(question no longer in the bank)');
      expect(text()).not.toContain('attempt ');
    });

    it('shows the question’s formatting and runs nothing hidden in it', async () => {
      const w = window as unknown as { __disputeRan?: boolean };
      await open([
        row('amy', {
          questionText:
            '<p><strong>Bold</strong></p><img src="x" onerror="window.__disputeRan = true"><script>window.__disputeRan = true</script>',
        }),
      ]);

      const questionText = root.querySelector('.rich-text') as HTMLElement;
      expect(questionText.querySelector('strong')?.textContent).toBe('Bold');
      expect(questionText.innerHTML).not.toContain('onerror');
      expect(questionText.querySelector('script')).toBeNull();
      expect(w.__disputeRan).toBeUndefined();
    });

    it('shows what the candidate wrote as text, never as markup', async () => {
      await open([row('amy', { reason: '<b>bold</b><img src="x" onerror="alert(1)">' })]);

      expect(root.querySelector('blockquote')?.textContent).toBe(
        '<b>bold</b><img src="x" onerror="alert(1)">',
      );
      expect(root.querySelector('blockquote b, blockquote img')).toBeNull();
    });

    it('says when there are no open disputes', async () => {
      await open([]);

      expect(root.querySelector('.empty-state')?.textContent).toContain('No open disputes.');
    });

    it('shows the reason when the queue cannot be read', async () => {
      await open('error');

      expect(root.querySelector('[role="alert"]')?.textContent).toContain('No access.');
    });
  });

  describe('grouping by question', () => {
    it('gathers the disputes of one question under it, so staff see how many candidates doubt it', async () => {
      await open([
        row('amy', { questionId: 'q1' }),
        row('ben', { questionId: 'q2', questionText: '<p>Largest planet?</p>' }),
        row('cy', { questionId: 'q1' }),
      ]);

      const [first, second] = groups();
      expect(groups()).toHaveLength(2);
      expect(first.querySelector('h3')?.textContent).toBe('2 candidates dispute this question');
      expect(rows(first).map((r) => r.textContent)).toEqual([
        expect.stringContaining('amy@example.com'),
        expect.stringContaining('cy@example.com'),
      ]);
      expect(first.textContent).toContain('Capital of France?');
      expect(second.querySelector('h3')?.textContent).toBe('1 candidate disputes this question');
      expect(second.textContent).toContain('ben@example.com');
      expect(second.textContent).toContain('Largest planet?');
    });

    it('shows the question once for the group, not once for each dispute', async () => {
      await open([row('amy'), row('ben'), row('cy')]);

      expect(root.querySelectorAll('.rich-text')).toHaveLength(1);
    });

    it('keeps the oldest dispute first, in the groups and within them', async () => {
      await open([
        row('amy', { questionId: 'q2' }),
        row('ben', { questionId: 'q1' }),
        row('cy', { questionId: 'q2' }),
      ]);

      const [first, second] = groups();
      expect(first.textContent).toContain('amy@example.com');
      expect(first.textContent).toContain('cy@example.com');
      expect(second.textContent).toContain('ben@example.com');
    });
  });

  describe('the status filter', () => {
    it('offers open, accepted and rejected, starting on open', async () => {
      await open([]);

      const select = root.querySelector('select') as HTMLSelectElement;
      expect(select.value).toBe('open');
      expect(Array.from(select.options).map((o) => o.textContent?.trim())).toEqual([
        'Open',
        'Accepted',
        'Rejected',
      ]);
      expect(root.querySelector('label[for="dispute-status"]')?.textContent).toContain(
        'Show disputes that are',
      );
    });

    it('reads the accepted disputes when chosen, and shows how each was settled with no actions on it', async () => {
      await open([row('amy')]);

      choose('accepted');
      const request = httpMock.expectOne(isList);
      expect(request.request.params.get('status')).toBe('accepted');
      request.flush([
        row('amy', {
          status: 'Accepted',
          resolvedAtUtc: '2026-10-06T12:00:00Z',
          resolutionNote: 'The key had Rome marked',
        }),
      ]);
      fixture.detectChanges();

      expect(groups()[0].querySelector('h3')?.textContent).toBe('1 dispute about this question');
      expect(text()).toContain('Accepted');
      expect(text()).toContain('Reason for the correction: The key had Rome marked');
      expect(root.querySelector('.badge')?.classList).toContain('badge--active');
      expect(buttons('Reject…')).toHaveLength(0);
      expect(correctButtons()).toHaveLength(0);
    });

    it('reads the rejected disputes when chosen, with the explanation that was given', async () => {
      await open([row('amy')]);

      choose('rejected');
      const request = httpMock.expectOne(isList);
      expect(request.request.params.get('status')).toBe('rejected');
      request.flush([
        row('amy', {
          status: 'Rejected',
          resolvedAtUtc: '2026-10-06T12:00:00Z',
          resolutionNote: 'Rome was the capital then',
        }),
        row('ben', {
          status: 'Rejected',
          resolvedAtUtc: '2026-10-06T13:00:00Z',
          resolutionNote: null,
        }),
      ]);
      fixture.detectChanges();

      expect(groups()[0].querySelector('h3')?.textContent).toBe('2 disputes about this question');
      expect(text()).toContain('Explanation given: Rome was the capital then');
      expect(root.querySelector('.badge')?.classList).toContain('badge--inactive');
      expect(buttons('Reject…')).toHaveLength(0);
    });

    it('says which kind is missing when there are none', async () => {
      await open([row('amy')]);

      choose('accepted');
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();

      expect(root.querySelector('.empty-state')?.textContent).toContain('No accepted disputes.');
    });

    it('waits while the other status is read, instead of showing the old list under it', async () => {
      await open([row('amy')]);

      choose('rejected');

      expect(text()).toContain('Loading…');
      expect(text()).not.toContain('amy@example.com');
      httpMock.expectOne(isList).flush([]);
    });

    it('reads nothing when the status chosen is the one already shown', async () => {
      await open([row('amy')]);

      choose('open');

      httpMock.expectNone(isList);
      expect(text()).toContain('amy@example.com');
    });

    it('is not overwritten by a slower read of a status chosen before', async () => {
      await open([]);

      choose('accepted');
      choose('rejected');
      const [stale, current] = httpMock.match(isList);
      expect(stale.cancelled).toBe(true);
      expect(current.request.params.get('status')).toBe('rejected');
      current.flush([row('amy', { status: 'Rejected', resolutionNote: 'No' })]);
      fixture.detectChanges();

      expect(text()).toContain('amy@example.com');
    });

    it('closes an open form, and clears the decisions made, when another status is chosen', async () => {
      await open([row('amy'), row('ben')]);
      press('Reject…', 0);
      type(root.querySelector('textarea') as HTMLTextAreaElement, 'No');
      press('Reject dispute');
      httpMock.expectOne(isReject('amy')).flush(row('amy', { status: 'Rejected' }));
      fixture.detectChanges();
      expect(results()).toHaveLength(1);

      choose('rejected');
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();
      choose('open');
      httpMock.expectOne(isList).flush([row('amy')]);
      fixture.detectChanges();

      expect(root.querySelector('form')).toBeNull();
      expect(results()).toHaveLength(0);
      expect(buttons('Reject…')).toHaveLength(1);
    });
  });

  describe('rejecting a dispute', () => {
    it('opens a form asking for an explanation, with a limit, and sends nothing yet', async () => {
      await open([row('amy')]);

      press('Reject…');

      const form = root.querySelector('form') as HTMLFormElement;
      const box = form.querySelector('textarea') as HTMLTextAreaElement;
      expect(form.getAttribute('aria-label')).toBe('Reject the dispute from amy@example.com');
      expect(form.querySelector('label')?.textContent).toContain(
        'Explanation to show the candidate',
      );
      expect(form.querySelector('label')?.getAttribute('for')).toBe(box.id);
      expect(box.maxLength).toBe(500);
      expect(box.getAttribute('aria-required')).toBe('true');
      httpMock.expectNone((r) => r.method === 'POST');
    });

    it('moves focus into the explanation box when the form opens, and back to its Reject button when cancelled', async () => {
      await open([row('amy')]);

      press('Reject…');
      await settle();
      expect(document.activeElement).toBe(root.querySelector('textarea'));

      press('Cancel');
      await settle();
      expect(document.activeElement).toBe(buttons('Reject…')[0]);
    });

    it('needs an explanation: the button stays locked for none or for spaces, and a forced submit sends nothing', async () => {
      await open([row('amy')]);
      press('Reject…');

      expect(buttons('Reject dispute')[0].disabled).toBe(true);
      type(root.querySelector('textarea') as HTMLTextAreaElement, '    ');
      expect(buttons('Reject dispute')[0].disabled).toBe(true);
      (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

      httpMock.expectNone((r) => r.method === 'POST');
    });

    it('sends the trimmed explanation, and turns that dispute into a line saying so, in its place, with no re-read of the queue', async () => {
      await open([row('amy'), row('ben')]);
      press('Reject…', 0);
      type(root.querySelector('textarea') as HTMLTextAreaElement, '  Rome was the capital then  ');

      press('Reject dispute');

      const post = httpMock.expectOne(isReject('amy'));
      expect(post.request.body).toEqual({ note: 'Rome was the capital then' });
      post.flush(row('amy', { status: 'Rejected', resolutionNote: 'Rome was the capital then' }));
      fixture.detectChanges();

      httpMock.expectNone(isList);
      expect(line(results()[0])).toBe(
        '✗ Rejected the dispute from amy@example.com. They will see your explanation.',
      );
      expect(root.querySelector('form')).toBeNull();
      const [amy, ben] = rows(groups()[0]);
      expect(amy.querySelector('form')).toBeNull();
      expect(ben.textContent).toContain('ben@example.com');
      expect(ben.querySelector('button')?.textContent?.trim()).toBe('Reject…');
    });

    it('moves focus to the line that says the dispute was rejected, so the next person to act is not lost at the top of the page', async () => {
      await open([row('amy'), row('ben')]);
      press('Reject…', 0);
      type(root.querySelector('textarea') as HTMLTextAreaElement, 'No');
      press('Reject dispute');

      httpMock.expectOne(isReject('amy')).flush(row('amy', { status: 'Rejected' }));
      await settle();

      expect(document.activeElement).toBe(results()[0]);
    });

    it('closes the form on Cancel and sends nothing', async () => {
      await open([row('amy')]);
      press('Reject…');
      type(root.querySelector('textarea') as HTMLTextAreaElement, 'A note');

      press('Cancel');

      expect(root.querySelector('form')).toBeNull();
      expect(buttons('Reject…')).toHaveLength(1);
      httpMock.expectNone((r) => r.method === 'POST');
    });

    it('opens the form for one dispute at a time, starting empty', async () => {
      await open([row('amy'), row('ben')]);
      press('Reject…', 0);
      type(root.querySelector('textarea') as HTMLTextAreaElement, 'Half-written');

      press('Reject…', 0);

      expect(root.querySelectorAll('form')).toHaveLength(1);
      expect((root.querySelector('textarea') as HTMLTextAreaElement).value).toBe('');
      expect(root.querySelector('form')?.getAttribute('aria-label')).toContain('ben@example.com');
    });

    it('keeps the dispute and shows the API’s reason on it when rejecting is refused', async () => {
      await open([row('amy'), row('ben')]);
      press('Reject…', 0);
      type(root.querySelector('textarea') as HTMLTextAreaElement, 'No');
      press('Reject dispute');

      httpMock
        .expectOne(isReject('amy'))
        .flush(
          { title: 'dispute_not_open', detail: 'This dispute was already answered.' },
          { status: 409, statusText: 'Conflict' },
        );
      fixture.detectChanges();

      const [amy, ben] = rows(groups()[0]);
      expect(amy.querySelector('[role="alert"]')?.textContent).toContain(
        'This dispute was already answered.',
      );
      expect(ben.querySelector('[role="alert"]')).toBeNull();
      expect(root.querySelector('form')).not.toBeNull();
      expect(buttons('Reject dispute')[0].disabled).toBe(false);
      expect(results()).toHaveLength(0);
      httpMock.expectNone(isList);
    });

    it('works on one dispute at a time', async () => {
      await open([row('amy'), row('ben')]);
      press('Reject…', 0);
      type(root.querySelector('textarea') as HTMLTextAreaElement, 'No');

      press('Reject dispute');

      expect(buttons('Reject dispute')[0].disabled).toBe(true);
      expect(buttons('Reject…').every((b) => b.disabled)).toBe(true);
      expect(buttons('Cancel')[0].disabled).toBe(true);
      httpMock.expectOne(isReject('amy')).flush(row('amy', { status: 'Rejected' }));
    });

    it('is offered to anyone who manages exams, whether or not they may correct a key', async () => {
      await open([row('amy')], [MANAGE_EXAMS]);

      expect(buttons('Reject…')).toHaveLength(1);
    });
  });

  describe('correcting the answer key', () => {
    it('is offered once for each question, to someone who may manage questions, and says how many open disputes it accepts', async () => {
      await open([
        row('amy', { questionId: 'q1' }),
        row('ben', { questionId: 'q2' }),
        row('cy', { questionId: 'q1' }),
      ]);

      expect(correctButtons().map((b) => b.textContent?.trim())).toEqual([
        'Correct the answer key — accepts 2 open disputes',
        'Correct the answer key — accepts 1 open dispute',
      ]);
    });

    it('is not offered to someone who may not manage questions, who can still reject', async () => {
      await open([row('amy')], [MANAGE_EXAMS]);

      expect(correctButtons()).toHaveLength(0);
      expect(root.querySelector('app-answer-key-correction')).toBeNull();
      expect(buttons('Reject…')).toHaveLength(1);
    });

    it('opens a panel that reads the question and starts from its current key', async () => {
      await open([row('amy')]);

      pressCorrect();
      httpMock.expectOne(isQuestionGet('q1')).flush(question());
      fixture.detectChanges();

      const panel = root.querySelector('app-answer-key-correction') as HTMLElement;
      expect(panel).not.toBeNull();
      const radios = Array.from(panel.querySelectorAll<HTMLInputElement>('input[type="radio"]'));
      expect(radios.map((r) => r.checked)).toEqual([true, false]);
      expect(panel.textContent).toContain('A. Rome');
      expect(panel.textContent).toContain('B. Paris');
      expect(correctButtons()).toHaveLength(0);
    });

    it('sends the corrected key with the reason, shows what it did above the question, and turns its open disputes into accepted lines in place', async () => {
      await open([row('amy'), row('ben', { questionId: 'q2' }), row('cy')]);
      pressCorrect(0);
      httpMock.expectOne(isQuestionGet('q1')).flush(question());
      fixture.detectChanges();
      (
        root.querySelectorAll(
          'app-answer-key-correction input[type="radio"]',
        )[1] as HTMLInputElement
      ).click();
      type(
        root.querySelector('app-answer-key-correction textarea') as HTMLTextAreaElement,
        'The key marked Rome',
      );

      press('Correct the answer key');

      const post = httpMock.expectOne(isCorrection('q1'));
      expect(post.request.body).toEqual({
        correctOptionIds: ['o2'],
        reason: 'The key marked Rome',
      });
      post.flush({ keyChanged: true, attemptsRescored: 3 });
      await settle();

      expect(root.querySelector('app-answer-key-correction')).toBeNull();
      const correctionLine = results()[0];
      expect(correctionLine.classList).toContain('dispute-result--correction');
      expect(line(correctionLine)).toBe(
        '✓ Answer key corrected. 3 results rescored. The open disputes about this question were accepted.',
      );
      expect(results().map(line)).toEqual([
        '✓ Answer key corrected. 3 results rescored. The open disputes about this question were accepted.',
        '✓ Accepted the dispute from amy@example.com.',
        '✓ Accepted the dispute from cy@example.com.',
      ]);
      expect(document.activeElement).toBe(correctionLine);

      const refresh = httpMock.expectOne(isList);
      expect(refresh.request.params.get('status')).toBe('open');
      refresh.flush([row('ben', { questionId: 'q2' })]);
      await settle();

      expect(results().map(line)).toHaveLength(3);
      expect(groups()[0].querySelector('h3')?.textContent).toBe(
        'Every dispute about this question has been answered.',
      );
      expect(root.querySelector('.empty-state')).toBeNull();
      expect(correctButtons().map((b) => b.textContent?.trim())).toEqual([
        'Correct the answer key — accepts 1 open dispute',
      ]);
    });

    it('says "1 result" when one was rescored', async () => {
      await open([row('amy')]);
      pressCorrect();
      httpMock.expectOne(isQuestionGet('q1')).flush(question());
      fixture.detectChanges();
      type(
        root.querySelector('app-answer-key-correction textarea') as HTMLTextAreaElement,
        'The key marked Rome',
      );

      press('Correct the answer key');
      httpMock.expectOne(isCorrection('q1')).flush({ keyChanged: true, attemptsRescored: 1 });
      fixture.detectChanges();
      httpMock.expectOne(isList).flush([]);
      fixture.detectChanges();

      expect(line(root.querySelector('.dispute-result--correction'))).toBe(
        '✓ Answer key corrected. 1 result rescored. The open disputes about this question were accepted.',
      );
    });

    it('says the key was already that, with nothing rescored, and leaves the disputes open with the correction still on offer', async () => {
      await open([row('amy')]);
      pressCorrect();
      httpMock.expectOne(isQuestionGet('q1')).flush(question());
      fixture.detectChanges();
      type(
        root.querySelector('app-answer-key-correction textarea') as HTMLTextAreaElement,
        'Checked again',
      );

      press('Correct the answer key');
      httpMock.expectOne(isCorrection('q1')).flush({ keyChanged: false, attemptsRescored: 0 });
      await settle();

      expect(line(root.querySelector('.dispute-result--correction'))).toBe(
        '– The answer key was already that, so nothing was changed and no results were rescored. The disputes stay open.',
      );
      expect(document.activeElement).toBe(root.querySelector('.dispute-result--correction'));
      httpMock.expectOne(isList).flush([row('amy')]);
      await settle();

      expect(text()).toContain('amy@example.com');
      expect(correctButtons().map((b) => b.textContent?.trim())).toEqual([
        'Correct the answer key — accepts 1 open dispute',
      ]);
    });

    it('keeps the panel and shows the API’s reason when the correction is refused', async () => {
      await open([row('amy')]);
      pressCorrect();
      httpMock.expectOne(isQuestionGet('q1')).flush(question());
      fixture.detectChanges();
      type(
        root.querySelector('app-answer-key-correction textarea') as HTMLTextAreaElement,
        'The key marked Rome',
      );
      press('Correct the answer key');

      httpMock
        .expectOne(isCorrection('q1'))
        .flush(
          { title: 'question_not_found', detail: 'There is no such question.' },
          { status: 404, statusText: 'Not Found' },
        );
      fixture.detectChanges();

      expect(root.querySelector('app-answer-key-correction [role="alert"]')?.textContent).toContain(
        'There is no such question.',
      );
      expect(root.querySelector('.dispute-result--correction')).toBeNull();
      httpMock.expectNone(isList);
    });

    it('closes the panel on Cancel, offers the action again, and gives focus back to it', async () => {
      await open([row('amy')]);
      pressCorrect();
      httpMock.expectOne(isQuestionGet('q1')).flush(question());
      fixture.detectChanges();

      press('Cancel');
      await settle();

      expect(root.querySelector('app-answer-key-correction')).toBeNull();
      expect(correctButtons()).toHaveLength(1);
      expect(document.activeElement).toBe(correctButtons()[0]);
      httpMock.expectNone(isCorrection('q1'));
    });

    it('opens the panel of one question at a time', async () => {
      await open([row('amy', { questionId: 'q1' }), row('ben', { questionId: 'q2' })]);
      pressCorrect(0);
      httpMock.expectOne(isQuestionGet('q1')).flush(question('q1'));
      fixture.detectChanges();

      pressCorrect(0);
      httpMock.expectOne(isQuestionGet('q2')).flush(question('q2'));
      fixture.detectChanges();

      expect(root.querySelectorAll('app-answer-key-correction')).toHaveLength(1);
      expect(groups()[1].querySelector('app-answer-key-correction')).not.toBeNull();
      expect(correctButtons()).toHaveLength(1);
    });

    it('is not offered on the accepted or rejected lists, even to someone who may manage questions', async () => {
      await open([row('amy')]);

      choose('accepted');
      httpMock
        .expectOne(isList)
        .flush([row('amy', { status: 'Accepted', resolutionNote: 'Key fixed' })]);
      fixture.detectChanges();

      expect(correctButtons()).toHaveLength(0);
    });
  });

  describe('in another language', () => {
    it('shows the title and the heading of a group in the language chosen', async () => {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, 'hi');
      await open([row('amy'), row('ben')]);

      expect(root.querySelector('h1')?.textContent).toBe(HI['admin.disputes.title']);
      expect(root.querySelector('h3')?.textContent).toBe(
        HI['admin.disputes.openHeading.other'].replace('{count}', '2'),
      );
    });

    it('shows a rejection in the language chosen, with the candidate’s address in it', async () => {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, 'mr');
      await open([row('amy')]);
      press(MR['admin.disputes.rejectOpen']);
      type(root.querySelector('textarea') as HTMLTextAreaElement, 'No');
      press(MR['admin.disputes.rejectSubmit']);

      httpMock.expectOne(isReject('amy')).flush(row('amy', { status: 'Rejected' }));
      fixture.detectChanges();

      expect(line(results()[0])).toBe(
        `✗ ${MR['admin.disputes.rejectedResult'].replace('{who}', 'amy@example.com')}`,
      );
    });
  });
});
