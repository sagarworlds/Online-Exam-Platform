import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AttemptRequestRow } from '../attempt-admin.models';
import { AttemptRequests } from './attempt-requests';

const row = (id: string, overrides: Partial<AttemptRequestRow> = {}): AttemptRequestRow => ({
  id,
  examId: 'e1',
  examName: 'Maths Final',
  candidateId: `c-${id}`,
  candidateEmail: `${id}@example.com`,
  message: 'Power cut',
  requestedAtUtc: '2026-10-05T05:00:00Z',
  status: 'Pending',
  decidedAtUtc: null,
  decisionNote: null,
  ...overrides,
});

describe('AttemptRequests', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<AttemptRequests>;
  let root: HTMLElement;

  const isList = (r: { method: string; url: string }) => r.method === 'GET' && r.url.includes('/v1/attempt-requests');

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AttemptRequests],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function open(rows: AttemptRequestRow[] | 'error') {
    fixture = TestBed.createComponent(AttemptRequests);
    fixture.detectChanges();
    const list = httpMock.expectOne(isList);
    expect(list.request.params.get('status')).toBe('pending');
    if (rows === 'error') {
      list.flush({ title: 'forbidden', detail: 'No access.' }, { status: 403, statusText: 'Forbidden' });
    } else {
      list.flush(rows);
    }
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const buttons = (label: string) => Array.from(root.querySelectorAll('button')).filter((b) => b.textContent?.trim() === label);
  const press = (label: string, index = 0) => {
    buttons(label)[index].click();
    fixture.detectChanges();
  };

  it('lists the waiting requests with who asked, for which exam, and why', () => {
    open([row('amy'), row('ben', { message: null, examName: null })]);

    const text = (root.textContent ?? '').replace(/\s+/g, ' ');
    expect(text).toContain('amy@example.com');
    expect(text).toContain('Maths Final');
    expect(text).toContain('Power cut');
    expect(text).toContain('They gave no reason.');
    expect(text).toContain('An exam that can no longer be read');
  });

  it('says when nobody is waiting', () => {
    open([]);

    expect(root.textContent).toContain('No candidates are waiting for another attempt.');
  });

  it('shows the reason when the queue cannot be read', () => {
    open('error');

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('No access.');
  });

  it('approves one request, takes it out of the queue and says so', () => {
    open([row('amy'), row('ben')]);

    press('Give another attempt', 0);
    const post = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/attempt-requests/amy/approve'));
    post.flush(row('amy', { status: 'Approved' }));
    fixture.detectChanges();

    expect(root.textContent).toContain('Gave amy@example.com another attempt.');
    expect(root.textContent).not.toContain('amy@example.com asked');
    expect(buttons('Give another attempt').length).toBe(1);
  });

  it('keeps the request and shows the API’s reason on it when approving is refused', () => {
    open([row('amy')]);

    press('Give another attempt');
    httpMock
      .expectOne((r) => r.method === 'POST' && r.url.endsWith('/amy/approve'))
      .flush({ title: 'exam_closed', detail: 'The exam is closed, so another attempt could never be used.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('could never be used');
    expect(buttons('Give another attempt').length).toBe(1);
  });

  it('declines with the typed reason, which the candidate will see', () => {
    open([row('amy')]);
    press('Decline…');
    const box = root.querySelector('textarea') as HTMLTextAreaElement;
    box.value = '  Speak to your teacher  ';
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    press('Decline request');

    const post = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/amy/decline'));
    expect(post.request.body).toEqual({ note: 'Speak to your teacher' });
    post.flush(row('amy', { status: 'Declined' }));
    fixture.detectChanges();

    expect(root.textContent).toContain('Declined the request from amy@example.com.');
    expect(root.textContent).toContain('No candidates are waiting');
  });

  it('declines with no note when none was typed, and Cancel sends nothing', () => {
    open([row('amy')]);
    press('Decline…');
    press('Cancel');
    httpMock.expectNone((r) => r.method === 'POST');

    press('Decline…');
    press('Decline request');

    expect(httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/amy/decline')).request.body).toEqual({ note: null });
  });

  it('works on one request at a time', () => {
    open([row('amy'), row('ben')]);

    press('Give another attempt', 0);

    expect(buttons('Give another attempt').every((b) => b.disabled)).toBe(true);
    httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/amy/approve')).flush(row('amy', { status: 'Approved' }));
  });
});
