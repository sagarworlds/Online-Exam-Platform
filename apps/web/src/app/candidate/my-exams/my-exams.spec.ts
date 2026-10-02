import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MyExams } from './my-exams';

describe('MyExams', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MyExams],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  const exam = (overrides: Record<string, unknown>) => ({
    examId: 'e1',
    name: 'Maths Final',
    description: null,
    startUtc: '2026-10-05T04:30:00Z',
    endUtc: '2026-10-05T07:30:00Z',
    lateEntryDeadlineUtc: null,
    durationSeconds: 5400,
    questionCount: 20,
    state: 'Open',
    ...overrides,
  });

  it('lists the exams with their state, size and duration', () => {
    const fixture = TestBed.createComponent(MyExams);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url.endsWith('/v1/me/exams')).flush([
      exam({}),
      exam({ examId: 'e2', name: 'Physics', state: 'NotOpen', durationSeconds: null, questionCount: 5 }),
      exam({ examId: 'e3', name: 'Chemistry', state: 'Closed' }),
    ]);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Maths Final');
    expect(text).toContain('20 questions, 90 minutes');
    expect(text).toContain('Not open yet');
    expect(text).toContain('Closed');
    expect(text).toContain('5 questions');
  });

  it('tells a candidate with no exams how to get one', () => {
    const fixture = TestBed.createComponent(MyExams);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url.endsWith('/v1/me/exams')).flush([]);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('invitation e-mail');
  });
});
