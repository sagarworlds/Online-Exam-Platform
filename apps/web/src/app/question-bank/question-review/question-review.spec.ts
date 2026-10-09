import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthSessionService } from '../../auth/auth-session.service';
import { QuestionStatus, ReviewEntry } from '../question.models';
import { QuestionReview } from './question-review';

describe('QuestionReview', () => {
  let fixture: ComponentFixture<QuestionReview>;
  let root: HTMLElement;
  let httpMock: HttpTestingController;
  let changes: QuestionStatus[];

  const log = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/review-log');
  const post = (suffix: string) => (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith(suffix);

  const entry = (over: Partial<ReviewEntry> = {}): ReviewEntry => ({
    id: crypto.randomUUID(),
    kind: 'commented',
    byLabel: 'reviewer@example.com',
    comment: 'A note',
    versionNumber: 1,
    statusAfter: 'in_review',
    createdAtUtc: '2026-10-06T09:00:00Z',
    ...over,
  });

  async function show(status: QuestionStatus, permissions: string[]) {
    await TestBed.configureTestingModule({
      imports: [QuestionReview],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthSessionService, useValue: { hasPermission: (code: string) => permissions.includes(code) } },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(QuestionReview);
    fixture.componentRef.setInput('questionId', 'q1');
    fixture.componentRef.setInput('status', status);
    changes = [];
    fixture.componentInstance.statusChanged.subscribe((s) => changes.push(s));
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  afterEach(() => httpMock.verify());

  const text = () => (root.textContent ?? '').replace(/\s+/g, ' ');
  const button = (label: string) => Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement | undefined;
  const type = (value: string) => {
    const box = root.querySelector('textarea') as HTMLTextAreaElement;
    box.value = value;
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const open = (entries: ReviewEntry[] = []) => {
    button(`Review: ${fixture.componentInstance['statusLabels'][fixture.componentInstance.status()]}`)!.click();
    httpMock.expectOne(log).flush(entries);
    fixture.detectChanges();
  };

  const MANAGE = 'question.manage';
  const READ = 'question.read';
  const REVIEW = 'question.review';

  it('shows the status on a closed button, and reads the thread only when opened', async () => {
    await show('in_review', [READ]);

    expect(text()).toContain('Review: In review');
    httpMock.expectNone(log);

    open([entry({ kind: 'submitted', byLabel: 'author@example.com', comment: 'Please look', versionNumber: 2 })]);

    expect(text()).toContain('author@example.com put it forward for review');
    expect(text()).toContain('Please look');
    expect(text()).toContain('version 2');
  });

  it('reads the thread once, however often the panel is opened and closed', async () => {
    await show('draft', [READ]);
    open();

    button('Review: Draft')!.click();
    button('Review: Draft')!.click();
    fixture.detectChanges();

    httpMock.expectNone(log);
    expect(text()).toContain('Nothing has been said about this question yet.');
  });

  it('says why the thread could not be read', async () => {
    await show('draft', [READ]);

    button('Review: Draft')!.click();
    httpMock.expectOne(log).flush({ title: 'forbidden', detail: 'No permission.' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(text()).toContain('No permission.');
  });

  it.each([
    ['draft', [MANAGE, READ, REVIEW], ['Put forward for review', 'Retire']],
    ['in_review', [MANAGE, READ, REVIEW], ['Approve', 'Send back', 'Retire']],
    ['approved', [MANAGE, READ, REVIEW], ['Retire']],
    ['retired', [MANAGE, READ, REVIEW], ['Restore as a draft']],
    ['in_review', [READ], []],
    ['in_review', [READ, REVIEW], ['Approve', 'Send back']],
    ['draft', [READ, MANAGE], ['Put forward for review', 'Retire']],
  ] as [QuestionStatus, string[], string[]][])('a question that is %s offers %j the steps %j', async (status, permissions, expected) => {
    await show(status, permissions);
    open();

    const steps = Array.from(root.querySelectorAll('.actions button')).map((b) => b.textContent?.trim()).filter((t) => t !== 'Add comment');

    expect(steps).toEqual(expected);
  });

  it('offers no comment box to someone who may neither comment nor take a step', async () => {
    await show('draft', []);
    open();

    expect(root.querySelector('textarea')).toBeNull();
  });

  it('takes a step with the comment, shows it in the thread and reports the new status', async () => {
    await show('draft', [MANAGE, READ]);
    open();
    type('  Ready for you  ');

    button('Put forward for review')!.click();
    const req = httpMock.expectOne(post('/q1/submit-for-review'));
    expect(req.request.body).toEqual({ comment: 'Ready for you' });
    req.flush({ status: 'in_review', entry: entry({ kind: 'submitted', comment: 'Ready for you' }) });
    fixture.detectChanges();

    expect(changes).toEqual(['in_review']);
    expect(text()).toContain('put it forward for review');
    expect((root.querySelector('textarea') as HTMLTextAreaElement).value).toBe('');
  });

  it('will not send a question back without a reason', async () => {
    await show('in_review', [READ, REVIEW]);
    open();

    expect(button('Send back')!.disabled).toBe(true);
    expect(button('Approve')!.disabled).toBe(false);

    type('  ');
    expect(button('Send back')!.disabled).toBe(true);
    type('Option B is also right');
    expect(button('Send back')!.disabled).toBe(false);
  });

  it('adds a comment without changing the status', async () => {
    await show('in_review', [READ]);
    open();
    type('One more thought');

    button('Add comment')!.click();
    const req = httpMock.expectOne(post('/q1/comments'));
    expect(req.request.body).toEqual({ comment: 'One more thought' });
    req.flush({ status: 'in_review', entry: entry({ comment: 'One more thought' }) });
    fixture.detectChanges();

    expect(changes).toEqual([]);
    expect(text()).toContain('One more thought');
  });

  it('says why a step was refused, and keeps what was typed', async () => {
    await show('in_review', [READ, REVIEW]);
    open();
    type('Looks fine');

    button('Approve')!.click();
    httpMock.expectOne(post('/q1/approve')).flush({ title: 'invalid_question_status', detail: 'Only a question that is in review can be approved.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(text()).toContain('Only a question that is in review can be approved.');
    expect((root.querySelector('textarea') as HTMLTextAreaElement).value).toBe('Looks fine');
    expect(changes).toEqual([]);
  });

  it('cannot take a second step while one is running', async () => {
    await show('in_review', [READ, REVIEW]);
    open();

    button('Approve')!.click();
    fixture.detectChanges();

    expect(button('Approve')!.disabled).toBe(true);
    httpMock.expectOne(post('/q1/approve')).flush({ status: 'approved', entry: entry({ kind: 'approved', statusAfter: 'approved' }) });
  });
});
