import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { ExamScheduler } from './exam-scheduler';

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

  function setup() {
    const fixture = TestBed.createComponent(ExamScheduler);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const type = (selector: string, value: string) => {
      const el = root.querySelector(selector) as HTMLInputElement;
      el.value = value;
      el.dispatchEvent(new Event('input'));
    };
    return { fixture, root, type };
  }

  it('keeps Save disabled until both the start and the end are given', () => {
    const { fixture, root, type } = setup();
    const save = root.querySelector('button.primary') as HTMLButtonElement;
    expect(save.disabled).toBe(true);

    type('#start-time', '2026-10-05T10:00');
    fixture.detectChanges();
    expect(save.disabled).toBe(true);

    type('#end-time', '2026-10-05T13:00');
    fixture.detectChanges();
    expect(save.disabled).toBe(false);
  });

  it('sends UTC instants, the duration and the time zone, then returns to the editor', () => {
    const { fixture, root, type } = setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    type('#start-time', '2026-10-05T10:00');
    type('#end-time', '2026-10-05T13:00');
    type('#duration', '90');
    type('#late-entry', '2026-10-05T10:30');
    fixture.detectChanges();

    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const put = httpMock.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/v1/exams/exam-1/schedule'));
    expect(put.request.body).toEqual({
      scheduledStartTime: new Date('2026-10-05T10:00').toISOString(),
      scheduledEndTime: new Date('2026-10-05T13:00').toISOString(),
      timeZone: 'Asia/Kolkata',
      durationMinutes: 90,
      lateEntryDeadline: new Date('2026-10-05T10:30').toISOString(),
    });
    put.flush({});
    expect(navigate).toHaveBeenCalledWith(['/exams', 'exam-1']);
  });

  it('sends null for the optional fields left blank', () => {
    const { fixture, root, type } = setup();
    type('#start-time', '2026-10-05T10:00');
    type('#end-time', '2026-10-05T13:00');
    fixture.detectChanges();

    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const body = httpMock.expectOne((r) => r.method === 'PUT').request.body;
    expect(body.durationMinutes).toBeNull();
    expect(body.lateEntryDeadline).toBeNull();
  });

  it('shows the API error when the schedule is refused', () => {
    const { fixture, root, type } = setup();
    type('#start-time', '2026-10-05T13:00');
    type('#end-time', '2026-10-05T10:00');
    fixture.detectChanges();
    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    httpMock
      .expectOne((r) => r.method === 'PUT')
      .flush({ title: 'invalid_exam_config', detail: 'Invalid exam config: The end must be after the start.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(root.textContent).toContain('The end must be after the start.');
  });
});
