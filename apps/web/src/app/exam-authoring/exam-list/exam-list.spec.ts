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

  it('says what each exam draws its questions from', () => {
    const fixture = TestBed.createComponent(ExamList);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.method === 'GET' && r.url.endsWith('/v1/exams')).flush([
      { id: 'e1', name: 'Free', description: null, status: 'Draft', isScheduled: false, scope: { type: 'Independent', bookId: null, bookName: null, className: null, chapters: [] } },
      { id: 'e2', name: 'Book test', description: null, status: 'Draft', isScheduled: false, scope: { type: 'Book', bookId: 'b1', bookName: 'Maths Grade 10', className: null, chapters: [] } },
      { id: 'e3', name: 'Chapter test', description: null, status: 'Draft', isScheduled: false, scope: { type: 'Chapters', bookId: 'b1', bookName: 'Maths Grade 10', className: null, chapters: [{ id: 'c1', title: 'Algebra' }] } },
      { id: 'e4', name: 'Olympiad book', description: null, status: 'Draft', isScheduled: false, scope: { type: 'Book', bookId: 'b2', bookName: 'English', className: '4th', chapters: [] } },
      { id: 'e5', name: 'Olympiad chapters', description: null, status: 'Draft', isScheduled: false, scope: { type: 'Chapters', bookId: 'b2', bookName: 'English', className: '4th', chapters: [{ id: 'c5', title: 'Nouns' }] } },
    ]);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Questions from: Any question in the bank');
    expect(text).toContain('Questions from: The whole book Maths Grade 10');
    expect(text).toContain('Questions from: Maths Grade 10: Algebra');
    // A book that has a class is shown with it, since the same book name can exist under several classes.
    expect(text).toContain('Questions from: The whole book English (4th)');
    expect(text).toContain('Questions from: English (4th): Nouns');
  });

  it('says so when there are no exams', () => {
    const fixture = TestBed.createComponent(ExamList);
    fixture.detectChanges();
    httpMock.expectOne((r) => r.method === 'GET').flush([]);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No exams yet.');
  });
});
