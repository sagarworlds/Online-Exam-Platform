import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { ToastService } from '../../shared/toast/toast.service';
import { ExamScheduler } from './exam-scheduler';

function examBody(overrides: Record<string, unknown> = {}) {
  return {
    id: 'exam-1',
    seriesId: null,
    name: 'Maths Final',
    description: null,
    status: 'Draft',
    config: {
      totalTimeSeconds: null,
      maxAttempts: 1,
      resultReleaseMode: 'Instant',
      resultReleaseTime: null,
      markingScheme: { correctMarks: 1, incorrectMarks: 0, unattemptedMarks: 0 },
    },
    scheduledStartTime: '0001-01-01T00:00:00Z',
    scheduledEndTime: '0001-01-01T00:00:00Z',
    lateEntryDeadline: null,
    timeZone: 'Asia/Kolkata',
    createdBy: 'u1',
    createdAt: '2026-10-02T00:00:00Z',
    updatedAt: '2026-10-02T00:00:00Z',
    isScheduled: false,
    sections: [],
    scope: { type: 'Independent', bookId: null, bookName: null, className: null, chapters: [] },
    ...overrides,
  };
}

describe('ExamScheduler', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ExamScheduler],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'exam-1' }) } } },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  /** Opens the page and answers the read of the exam with `exam` (an exam with no schedule yet by default). */
  function setup(exam: Record<string, unknown> | 'fail' = {}) {
    const fixture = TestBed.createComponent(ExamScheduler);
    fixture.detectChanges();
    const read = httpMock.expectOne((r) => r.method === 'GET' && r.url.endsWith('/v1/exams/exam-1'));
    if (exam === 'fail') {
      read.flush({}, { status: 500, statusText: 'Server Error' });
    } else {
      read.flush(examBody(exam));
    }
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const control = (label: string) => {
      const found = Array.from(root.querySelectorAll('label')).find((l) => l.textContent?.trim().startsWith(label));
      return root.querySelector(`#${found?.getAttribute('for')}`) as HTMLInputElement & HTMLSelectElement;
    };
    const type = (label: string, value: string) => {
      const el = control(label);
      el.value = value;
      el.dispatchEvent(new Event(el.tagName === 'SELECT' ? 'change' : 'input'));
      fixture.detectChanges();
    };
    const save = () => {
      (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
      fixture.detectChanges();
    };
    const errors = () => Array.from(root.querySelectorAll('.field-error')).map((e) => e.textContent?.trim());
    return { fixture, root, control, type, save, errors };
  }

  it('does not switch Save off: pressing it with the window empty says what is missing, and sends nothing', () => {
    const { root, save, errors } = setup();
    const button = root.querySelector('button.primary') as HTMLButtonElement;
    expect(button.disabled).toBe(false);

    save();

    expect(errors()).toEqual(['Enter when the exam opens.', 'Enter when the exam closes.']);
    httpMock.expectNone((r) => r.method === 'PUT');
  });

  it('marks the two times that must be given as required and the other two as optional', () => {
    const { root } = setup();
    const label = (text: string) => Array.from(root.querySelectorAll('label')).find((l) => l.textContent?.includes(text))?.textContent;

    expect(label('Opens')).toContain('*');
    expect(label('Closes')).toContain('*');
    expect(label('Minutes per attempt')).toContain('(optional)');
    expect(label('Latest start')).toContain('(optional)');
  });

  it('sends the times as the instants they are in the zone chosen, then says it is saved and returns to the editor', () => {
    const { type, save } = setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    type('Time zone', 'Asia/Kolkata');
    type('Opens', '2026-10-05T10:00');
    type('Closes', '2026-10-05T13:00');
    type('Minutes per attempt', '90');
    type('Latest start', '2026-10-05T10:30');

    save();

    const put = httpMock.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/v1/exams/exam-1/schedule'));
    expect(put.request.body).toEqual({
      scheduledStartTime: '2026-10-05T04:30:00.000Z',
      scheduledEndTime: '2026-10-05T07:30:00.000Z',
      timeZone: 'Asia/Kolkata',
      durationMinutes: 90,
      lateEntryDeadline: '2026-10-05T05:00:00.000Z',
    });
    put.flush({});
    expect(navigate).toHaveBeenCalledWith(['/exams', 'exam-1']);
    expect(TestBed.inject(ToastService).toasts().map((t) => [t.kind, t.message])).toEqual([['success', 'The schedule is saved.']]);
  });

  it('reads the times in the zone chosen, not in the browser’s: the same clock times in New York are a different instant', () => {
    const { type, save } = setup();
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    type('Time zone', 'America/New_York');
    type('Opens', '2026-10-05T10:00');
    type('Closes', '2026-10-05T13:00');

    save();

    const body = httpMock.expectOne((r) => r.method === 'PUT').request.body;
    // New York is on summer time (UTC-4) on 5 October.
    expect(body.scheduledStartTime).toBe('2026-10-05T14:00:00.000Z');
    expect(body.scheduledEndTime).toBe('2026-10-05T17:00:00.000Z');
    expect(body.timeZone).toBe('America/New_York');
  });

  it('sends null for the optional fields left blank', () => {
    const { type, save } = setup();
    type('Opens', '2026-10-05T10:00');
    type('Closes', '2026-10-05T13:00');

    save();

    const body = httpMock.expectOne((r) => r.method === 'PUT').request.body;
    expect(body.durationMinutes).toBeNull();
    expect(body.lateEntryDeadline).toBeNull();
  });

  it('says so when the exam would close before it opens, and sends nothing', () => {
    const { type, save, errors } = setup();
    type('Opens', '2026-10-05T13:00');
    type('Closes', '2026-10-05T10:00');

    save();

    expect(errors()).toEqual(['The exam must close after it opens.']);
    httpMock.expectNone((r) => r.method === 'PUT');
  });

  it('says so when the latest start is outside the window, or the minutes are not a whole number of 1 or more', () => {
    const { type, save, errors } = setup();
    type('Opens', '2026-10-05T10:00');
    type('Closes', '2026-10-05T13:00');
    type('Latest start', '2026-10-05T14:00');
    type('Minutes per attempt', '0');

    save();

    expect(errors()).toEqual([
      'Enter a whole number of minutes, 1 or more, or leave it empty.',
      'The latest start must be between when the exam opens and when it closes.',
    ]);
    httpMock.expectNone((r) => r.method === 'PUT');
  });

  it('moves focus to the first field that is wrong', async () => {
    const { fixture, root, save, type } = setup();
    document.body.appendChild(root);
    type('Opens', '2026-10-05T10:00');

    save();
    await fixture.whenStable();

    const wrong = root.querySelector('[aria-invalid="true"]');
    expect(wrong).not.toBeNull();
    expect(document.activeElement).toBe(wrong);
    root.remove();
  });

  it('does not name a field as wrong before Save is pressed', () => {
    const { type, errors } = setup();
    type('Opens', '2026-10-05T13:00');
    type('Closes', '2026-10-05T10:00');

    expect(errors()).toEqual([]);
  });

  describe('an exam that already has a schedule', () => {
    const scheduled = {
      isScheduled: true,
      scheduledStartTime: '2026-10-05T04:30:00Z',
      scheduledEndTime: '2026-10-05T07:30:00Z',
      lateEntryDeadline: '2026-10-05T05:00:00Z',
      timeZone: 'Asia/Kolkata',
      config: { ...examBody().config, totalTimeSeconds: 5400 },
    };

    it('opens with that schedule filled in, as clock times in the exam’s own zone', () => {
      const { control } = setup(scheduled);

      expect(control('Time zone').value).toBe('Asia/Kolkata');
      expect(control('Opens').value).toBe('2026-10-05T10:00');
      expect(control('Closes').value).toBe('2026-10-05T13:00');
      expect(control('Minutes per attempt').value).toBe('90');
      expect(control('Latest start').value).toBe('2026-10-05T10:30');
    });

    it('saves the same schedule back unchanged when nothing is touched', () => {
      const { save } = setup(scheduled);
      vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

      save();

      expect(httpMock.expectOne((r) => r.method === 'PUT').request.body).toEqual({
        scheduledStartTime: '2026-10-05T04:30:00.000Z',
        scheduledEndTime: '2026-10-05T07:30:00.000Z',
        timeZone: 'Asia/Kolkata',
        durationMinutes: 90,
        lateEntryDeadline: '2026-10-05T05:00:00.000Z',
      });
    });

    it('offers the exam’s zone even when it is not one of the usual ones', () => {
      const { control } = setup({ ...scheduled, timeZone: 'Asia/Tokyo', scheduledStartTime: '2026-10-05T01:00:00Z', scheduledEndTime: '2026-10-05T04:00:00Z', lateEntryDeadline: null });

      expect(control('Time zone').value).toBe('Asia/Tokyo');
      expect(control('Opens').value).toBe('2026-10-05T10:00');
    });
  });

  it('says the current schedule is not shown when the exam could not be read, and still lets one be entered', () => {
    const { root, type, save } = setup('fail');
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    expect(root.textContent).toContain('The exam could not be read, so its current schedule is not shown.');

    type('Opens', '2026-10-05T10:00');
    type('Closes', '2026-10-05T13:00');
    save();
    httpMock.expectOne((r) => r.method === 'PUT').flush({});
  });

  it('shows the API’s reason when the schedule is refused, and stays on the page', () => {
    const { fixture, root, type, save } = setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    type('Opens', '2026-10-05T10:00');
    type('Closes', '2026-10-05T13:00');
    save();

    httpMock
      .expectOne((r) => r.method === 'PUT')
      .flush({ title: 'invalid_exam_config', detail: 'Invalid exam config: The exam is already published.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('The exam is already published.');
    expect(navigate).not.toHaveBeenCalled();
  });
});
