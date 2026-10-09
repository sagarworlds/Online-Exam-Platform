import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { I18nService, LANGUAGE_STORAGE_KEY } from '../../i18n/i18n.service';
import { HI } from '../../i18n/messages.hi';
import { MR } from '../../i18n/messages.mr';
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

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

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
  const line = (element: Element | null | undefined) =>
    (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const button = (label: string) =>
    Array.from(root.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === label,
    ) as HTMLButtonElement;
  const press = (label: string) => {
    button(label).click();
    fixture.detectChanges();
  };
  const field = (suffix: string) =>
    root.querySelector(`#accommodation-c1-${suffix}`) as HTMLInputElement & HTMLTextAreaElement;
  const type = (suffix: string, value: string) => {
    field(suffix).value = value;
    field(suffix).dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const tick = (label: string, checked = true) => {
    const box = Array.from(root.querySelectorAll('label.inline-check'))
      .find((l) => l.textContent?.includes(label))!
      .querySelector('input') as HTMLInputElement;
    box.checked = checked;
    box.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  };
  const submit = () => {
    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  };
  const facts = () =>
    Array.from(root.querySelectorAll('.accommodation__facts dd')).map((dd) =>
      dd.textContent?.trim(),
    );
  /** Lets the page draw what the last action changed, and any focus that waits for it, to be moved. */
  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('says none is given, and offers to give one', () => {
    show(row());

    expect(text()).toContain(
      'None. Extra time, a reader or scribe, or an alternate format can be given here.',
    );
    expect(button('Give an accommodation')).toBeDefined();
    expect(root.querySelector('form')).toBeNull();
  });

  it('shows what is given as labelled facts, with the note marked as staff only', () => {
    show(
      row(
        given({
          extraTimeMinutes: 45,
          readerScribe: true,
          alternateFormats: ['large_text', 'screen_reader'],
        }),
      ),
    );

    expect(
      Array.from(root.querySelectorAll('.accommodation__facts dt')).map((dt) =>
        dt.textContent?.trim(),
      ),
    ).toEqual(['Extra time', 'Reader or scribe', 'Formats']);
    expect(facts()).toEqual(['45 minutes', 'Allowed', 'Large text, Screen reader']);
    expect(text()).toContain('Note, for staff only: Certificate seen');
  });

  it('says the extra time is none, and that no reader is given, when they are not', () => {
    show(
      row(given({ extraTimeMinutes: 0, readerScribe: false, alternateFormats: ['high_contrast'] })),
    );

    expect(facts()).toEqual(['None', 'Not given', 'High contrast']);
  });

  it('uses the singular for one minute', () => {
    show(row(given({ extraTimeMinutes: 1 })));

    expect(facts()[0]).toBe('1 minute');
  });

  it('opens a form filled with what the candidate has, and can be cancelled without sending anything', async () => {
    show(
      row(
        given({
          extraTimeMinutes: 20,
          readerScribe: true,
          alternateFormats: ['high_contrast'],
          notes: 'n1',
        }),
      ),
    );

    press('Change');
    await settle();
    expect(document.activeElement).toBe(field('minutes'));
    expect(field('minutes').value).toBe('20');
    expect(field('notes').value).toBe('n1');
    expect((root.querySelectorAll('input[type="checkbox"]')[0] as HTMLInputElement).checked).toBe(
      true,
    );

    press('Cancel');
    await settle();
    expect(root.querySelector('form')).toBeNull();
    expect(document.activeElement).toBe(button('Change'));
    httpMock.expectNone(isRoute('PUT'));
  });

  it('leaves Save enabled, and names what is missing once it is pressed, sending nothing', () => {
    show(row());
    press('Give an accommodation');

    expect(button('Save accommodation').disabled).toBe(false);
    expect(text()).not.toContain('Give extra time, a reader or scribe, or a format.');

    press('Save accommodation');

    expect(text()).toContain('Give extra time, a reader or scribe, or a format.');
    httpMock.expectNone(isRoute('PUT'));

    tick('A reader or scribe may be used');
    expect(text()).not.toContain('Give extra time, a reader or scribe, or a format.');
  });

  it('names extra time that is not a whole number of minutes in range once Save is pressed, and clears it when fixed', () => {
    show(row());
    press('Give an accommodation');
    type('minutes', '721');
    expect(field('minutes').getAttribute('aria-invalid')).toBeNull();

    press('Save accommodation');
    expect(field('minutes').getAttribute('aria-invalid')).toBe('true');
    expect(text()).toContain('from 0 to 720');
    expect(field('minutes').getAttribute('aria-describedby')).toContain(
      'accommodation-c1-minutes-error',
    );
    httpMock.expectNone(isRoute('PUT'));

    type('minutes', '2.5');
    press('Save accommodation');
    expect(text()).toContain('from 0 to 720');

    type('minutes', '45');
    expect(text()).not.toContain('from 0 to 720');
    expect(field('minutes').getAttribute('aria-invalid')).toBeNull();
  });

  it('treats an empty minutes box as none', () => {
    show(row());
    press('Give an accommodation');
    tick('Large text');

    type('minutes', '');
    press('Save accommodation');

    const put = httpMock.expectOne(isRoute('PUT'));
    expect(put.request.body).toEqual({
      extraTimeMinutes: 0,
      readerScribe: false,
      alternateFormats: ['large_text'],
      notes: null,
    });
    put.flush(
      row(
        given({
          extraTimeMinutes: 0,
          readerScribe: false,
          alternateFormats: ['large_text'],
          notes: null,
        }),
      ),
    );
  });

  it('sends the whole accommodation, the formats in the order they are offered, and a trimmed note', async () => {
    show(row());
    press('Give an accommodation');
    type('minutes', '30');
    tick('A reader or scribe may be used');
    tick('Screen reader');
    tick('Large text');
    type('notes', '  Certificate seen  ');

    submit();

    const put = httpMock.expectOne(isRoute('PUT'));
    expect(put.request.body).toEqual({
      extraTimeMinutes: 30,
      readerScribe: true,
      alternateFormats: ['large_text', 'screen_reader'],
      notes: 'Certificate seen',
    });
    const saved = row(
      given({ extraTimeMinutes: 30, alternateFormats: ['large_text', 'screen_reader'] }),
    );
    put.flush(saved);
    await settle();

    expect(changes).toEqual([saved]);
    expect(root.querySelector('form')).toBeNull();
    expect(line(root.querySelector('.accommodation__notice'))).toBe(
      'Accommodation saved. A candidate who is sitting now has the extra time added to their attempt.',
    );
    expect(document.activeElement).toBe(root.querySelector('.accommodation__notice'));
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
    httpMock
      .expectOne(isRoute('PUT'))
      .flush(
        { title: 'candidate_not_enrolled', detail: 'This person is not enrolled in the exam.' },
        { status: 404, statusText: 'Not Found' },
      );
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

  it('asks before removing, inline, and removes only after the question is answered', async () => {
    show(row(given()));

    press('Remove');
    await settle();
    expect(text()).toContain(
      'Remove this accommodation? An attempt in progress keeps what it was given.',
    );
    expect(document.activeElement).toBe(root.querySelector('.accommodation__confirm'));
    httpMock.expectNone(isRoute('DELETE'));

    press('Remove');
    const del = httpMock.expectOne(isRoute('DELETE'));
    const after = row(null);
    del.flush(after);
    await settle();

    expect(changes).toEqual([after]);
    expect(line(root.querySelector('.accommodation__notice'))).toBe(
      'Accommodation removed. An attempt already in progress keeps the time it was given.',
    );
    expect(document.activeElement).toBe(root.querySelector('.accommodation__notice'));
  });

  it('keeps the accommodation when the question is answered No, sending nothing, and gives focus back to Remove', async () => {
    show(row(given()));
    press('Remove');

    press('Keep it');
    await settle();

    expect(root.querySelector('.accommodation__confirm')).toBeNull();
    expect(text()).not.toContain('Remove this accommodation?');
    expect(document.activeElement).toBe(button('Remove'));
    httpMock.expectNone(isRoute('DELETE'));
  });

  it('shows why it could not be removed', () => {
    show(row(given()));
    press('Remove');
    press('Remove');

    httpMock
      .expectOne(isRoute('DELETE'))
      .flush(
        {
          title: 'accommodation_not_found',
          detail: 'This candidate has no accommodation at this exam.',
        },
        { status: 404, statusText: 'Not Found' },
      );
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

  it('gives its labels, facts and results in the language chosen', async () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'hi');
    await TestBed.inject(I18nService).ready();
    show(
      row(given({ extraTimeMinutes: 15, readerScribe: false, alternateFormats: [], notes: null })),
    );

    expect(root.querySelector('.accommodation__title')?.textContent?.trim()).toBe(
      HI['admin.accommodation.title'],
    );
    expect(
      Array.from(root.querySelectorAll('.accommodation__facts dt')).map((dt) =>
        dt.textContent?.trim(),
      ),
    ).toEqual([
      HI['admin.accommodation.factExtraTime'],
      HI['admin.accommodation.factReader'],
      HI['admin.accommodation.factFormats'],
    ]);
    expect(facts()[0]).toBe(HI['admin.accommodation.minutes.other'].replace('{count}', '15'));
  });

  it('names a format and its result in Marathi, with the candidate’s own note as typed', async () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'mr');
    await TestBed.inject(I18nService).ready();
    show(row());
    press(MR['admin.accommodation.give']);
    tick(MR['admin.accommodation.format.large_text']);
    press(MR['admin.accommodation.save']);

    httpMock
      .expectOne(isRoute('PUT'))
      .flush(
        row(
          given({
            extraTimeMinutes: 0,
            readerScribe: false,
            alternateFormats: ['large_text'],
            notes: null,
          }),
        ),
      );
    await settle();

    expect(line(root.querySelector('.accommodation__notice'))).toBe(
      MR['admin.accommodation.saved'],
    );
  });
});
