import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Accommodation, ExamCandidateDto } from '../attempt-admin.models';
import { CandidateAccommodation } from './candidate-accommodation';

const isRoute = (method: string) => (r: { method: string; url: string }) =>
  r.method === method && r.url.endsWith('/v1/exams/e1/candidates/c1/accommodation');

const given = (overrides: Partial<Accommodation> = {}): Accommodation => ({
  extraTimeMinutes: 30,
  readerScribe: true,
  alternateFormats: ['large_text'],
  notes: 'Certificate seen',
  updatedAtUtc: '2026-10-07T04:30:00Z',
  ...overrides,
});

const row = (accommodation: Accommodation | null = null): ExamCandidateDto => ({
  candidateId: 'c1',
  email: 'student@example.com',
  attemptsAllowed: 1,
  attemptsUsed: 0,
  canGrant: false,
  attempts: [],
  accommodation,
});

describe('CandidateAccommodation', () => {
  let fixture: ComponentFixture<CandidateAccommodation>;
  let root: HTMLElement;
  let httpMock: HttpTestingController;
  let changes: ExamCandidateDto[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CandidateAccommodation],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function show(candidate: ExamCandidateDto) {
    fixture = TestBed.createComponent(CandidateAccommodation);
    fixture.componentRef.setInput('examId', 'e1');
    fixture.componentRef.setInput('candidate', candidate);
    changes = [];
    fixture.componentInstance.changed.subscribe((c) => changes.push(c));
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const text = () => (root.textContent ?? '').replace(/\s+/g, ' ');
  const button = (label: string) => Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;
  const press = (label: string) => {
    button(label).click();
    fixture.detectChanges();
  };
  const field = (suffix: string) => root.querySelector(`#accommodation-c1-${suffix}`) as HTMLInputElement & HTMLTextAreaElement;
  const type = (suffix: string, value: string) => {
    field(suffix).value = value;
    field(suffix).dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const tick = (label: string, checked = true) => {
    const box = Array.from(root.querySelectorAll('label.inline-check')).find((l) => l.textContent?.includes(label))!.querySelector('input') as HTMLInputElement;
    box.checked = checked;
    box.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  };
  const submit = () => {
    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  };

  it('says none is given, and offers to give one', () => {
    show(row());

    expect(text()).toContain('None.');
    expect(button('Give an accommodation')).toBeDefined();
    expect(root.querySelector('form')).toBeNull();
  });

  it('shows what is given, with the note marked as staff only', () => {
    show(row(given({ extraTimeMinutes: 45, readerScribe: true, alternateFormats: ['large_text', 'screen_reader'] })));

    expect(Array.from(root.querySelectorAll('.badge')).map((b) => b.textContent?.trim())).toEqual(['+45 minutes', 'Reader or scribe', 'Large text', 'Screen reader']);
    expect(text()).toContain('Note, for staff only: Certificate seen');
  });

  it('shows no minutes badge when no extra time is given', () => {
    show(row(given({ extraTimeMinutes: 0, readerScribe: false, alternateFormats: ['high_contrast'] })));

    expect(Array.from(root.querySelectorAll('.badge')).map((b) => b.textContent?.trim())).toEqual(['High contrast']);
  });

  it('opens a form filled with what the candidate has, and can be cancelled without sending anything', () => {
    show(row(given({ extraTimeMinutes: 20, readerScribe: true, alternateFormats: ['high_contrast'], notes: 'n1' })));

    press('Change');

    expect(field('minutes').value).toBe('20');
    expect(field('notes').value).toBe('n1');
    expect((root.querySelectorAll('input[type="checkbox"]')[0] as HTMLInputElement).checked).toBe(true);
    press('Cancel');
    expect(root.querySelector('form')).toBeNull();
    httpMock.expectNone(isRoute('PUT'));
  });

  it('cannot be saved while it gives nothing, and says what to give', () => {
    show(row());
    press('Give an accommodation');

    expect(button('Save accommodation').disabled).toBe(true);
    expect(text()).toContain('Give extra time, a reader or scribe, or a format.');

    tick('A reader or scribe may be used');
    expect(button('Save accommodation').disabled).toBe(false);
    expect(text()).not.toContain('Give extra time, a reader or scribe, or a format.');
  });

  it('refuses extra time that is not a whole number of minutes in range', () => {
    show(row());
    press('Give an accommodation');

    type('minutes', '721');
    expect(button('Save accommodation').disabled).toBe(true);
    expect(text()).toContain('from 0 to 720');

    type('minutes', '2.5');
    expect(button('Save accommodation').disabled).toBe(true);

    type('minutes', '45');
    expect(button('Save accommodation').disabled).toBe(false);
    expect(text()).not.toContain('from 0 to 720');
  });

  it('treats an empty minutes box as none', () => {
    show(row());
    press('Give an accommodation');
    tick('Large text');

    type('minutes', '');

    expect(button('Save accommodation').disabled).toBe(false);
  });

  it('sends the whole accommodation, the formats in the order they are offered, and a trimmed note', () => {
    show(row());
    press('Give an accommodation');
    type('minutes', '30');
    tick('A reader or scribe may be used');
    tick('Screen reader');
    tick('Large text');
    type('notes', '  Certificate seen  ');

    submit();

    const put = httpMock.expectOne(isRoute('PUT'));
    expect(put.request.body).toEqual({ extraTimeMinutes: 30, readerScribe: true, alternateFormats: ['large_text', 'screen_reader'], notes: 'Certificate seen' });
    const saved = row(given({ extraTimeMinutes: 30, alternateFormats: ['large_text', 'screen_reader'] }));
    put.flush(saved);
    fixture.detectChanges();

    expect(changes).toEqual([saved]);
    expect(root.querySelector('form')).toBeNull();
    expect(text()).toContain('Accommodation saved.');
  });

  it('sends no note rather than an empty one', () => {
    show(row());
    press('Give an accommodation');
    type('minutes', '10');
    type('notes', '   ');

    submit();

    const put = httpMock.expectOne(isRoute('PUT'));
    expect(put.request.body.notes).toBeNull();
    put.flush(row(given()));
  });

  it('shows why the API refused it, and keeps the form open with what was typed', () => {
    show(row());
    press('Give an accommodation');
    type('minutes', '30');

    submit();
    httpMock.expectOne(isRoute('PUT')).flush({ title: 'candidate_not_enrolled', detail: 'This person is not enrolled in the exam.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(root.querySelector('.error-message')?.textContent).toContain('not enrolled');
    expect(root.querySelector('form')).not.toBeNull();
    expect(field('minutes').value).toBe('30');
    expect(changes).toEqual([]);
  });

  it('does not send twice while a save is waiting', () => {
    show(row());
    press('Give an accommodation');
    type('minutes', '30');

    submit();
    submit();

    httpMock.expectOne(isRoute('PUT')).flush(row(given()));
  });

  it('removes it only after asking, then reports how the candidate stands', () => {
    show(row(given()));

    press('Remove');
    expect(text()).toContain('An attempt in progress keeps what it was given.');
    httpMock.expectNone(isRoute('DELETE'));

    press('Remove');
    const del = httpMock.expectOne(isRoute('DELETE'));
    const after = row(null);
    del.flush(after);
    fixture.detectChanges();

    expect(changes).toEqual([after]);
    expect(text()).toContain('Accommodation removed.');
  });

  it('backs out of removing without sending anything', () => {
    show(row(given()));
    press('Remove');

    (Array.from(root.querySelectorAll('[role="alertdialog"] button')).find((b) => b.textContent?.trim() === 'Cancel') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(text()).not.toContain('Remove it?');
    httpMock.expectNone(isRoute('DELETE'));
  });

  it('shows why it could not be removed', () => {
    show(row(given()));
    press('Remove');
    press('Remove');

    httpMock.expectOne(isRoute('DELETE')).flush({ title: 'accommodation_not_found', detail: 'This candidate has no accommodation at this exam.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(root.querySelector('.error-message')?.textContent).toContain('no accommodation');
    expect(changes).toEqual([]);
  });

  it('gives each candidate their own field ids, so a page of many has no duplicates', () => {
    show({ ...row(), candidateId: 'c2' });
    press('Give an accommodation');

    expect(root.querySelector('#accommodation-c2-minutes')).not.toBeNull();
    expect(root.querySelector('#accommodation-c1-minutes')).toBeNull();
  });
});
