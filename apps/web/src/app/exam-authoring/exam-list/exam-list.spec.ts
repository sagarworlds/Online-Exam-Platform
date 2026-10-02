import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ExamList } from './exam-list';

describe('ExamList', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ExamList],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('lists exams with their status and whether they are scheduled', () => {
    const fixture = TestBed.createComponent(ExamList);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.method === 'GET' && r.url.endsWith('/v1/exams')).flush([
      { id: 'e1', name: 'Maths Final', description: null, status: 'Published', isScheduled: true, scheduledStartTime: '2026-10-05T04:30:00Z' },
      { id: 'e2', name: 'Draft Exam', description: 'WIP', status: 'Draft', isScheduled: false, scheduledStartTime: '0001-01-01T00:00:00Z' },
    ]);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Maths Final');
    expect(text).toContain('Published');
    expect(text).toContain('Draft Exam');
    expect(text).toContain('Not scheduled yet');
    expect(text).not.toContain('0001');
  });

  it('says so when there are no exams', () => {
    const fixture = TestBed.createComponent(ExamList);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.method === 'GET').flush([]);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No exams yet.');
  });
});
