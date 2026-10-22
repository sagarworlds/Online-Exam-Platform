import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
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

  describe('the refresh icon', () => {
    const isRead = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/exams');
    const exam = (id: string, name: string) => ({ id, name, description: null, status: 'Draft', isScheduled: false });

    function open() {
      const fixture = TestBed.createComponent(ExamList);
      fixture.detectChanges();
      httpMock.expectOne(isRead).flush([exam('e1', 'Maths Final')]);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;
      return { fixture, root, icon: () => root.querySelector<HTMLButtonElement>('app-refresh-button button')! };
    }

    it('reads the exams again without blanking the list, and shows the exams added since', () => {
      const { fixture, root, icon } = open();

      icon().click();
      fixture.detectChanges();
      expect(icon().getAttribute('aria-busy')).toBe('true');
      expect(root.textContent).toContain('Maths Final');
      expect(root.textContent).not.toContain('Loading');

      httpMock.expectOne(isRead).flush([exam('e1', 'Maths Final'), exam('e2', 'Physics Mock')]);
      fixture.detectChanges();

      expect(root.textContent).toContain('Physics Mock');
      expect(icon().hasAttribute('aria-busy')).toBe(false);
    });

    it('keeps the list and says why when the read fails, and clears the reason once it works', () => {
      const { fixture, root, icon } = open();

      icon().click();
      httpMock.expectOne(isRead).flush({ title: 'server_error', detail: 'The exams are not available.' }, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      expect(root.textContent).toContain('Maths Final');
      expect(root.textContent).toContain('The exams are not available.');
      expect(icon().hasAttribute('aria-busy')).toBe(false);

      icon().click();
      httpMock.expectOne(isRead).flush([exam('e1', 'Maths Final')]);
      fixture.detectChanges();
      expect(root.textContent).not.toContain('The exams are not available.');
    });

    it('is there when the first read failed, and recovers the list', () => {
      const fixture = TestBed.createComponent(ExamList);
      fixture.detectChanges();
      httpMock.expectOne(isRead).flush({ title: 'server_error', detail: 'The exams are not available.' }, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;

      root.querySelector<HTMLButtonElement>('app-refresh-button button')!.click();
      httpMock.expectOne(isRead).flush([exam('e1', 'Maths Final')]);
      fixture.detectChanges();

      expect(root.textContent).toContain('Maths Final');
      expect(root.textContent).not.toContain('The exams are not available.');
    });
  });

  describe('finding an exam', () => {
    const isRead = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/exams');
    const exam = (id: string, name: string, status = 'Draft', description: string | null = null) => ({ id, name, description, status, isScheduled: false });

    function open(exams: ReturnType<typeof exam>[]) {
      const fixture = TestBed.createComponent(ExamList);
      fixture.detectChanges();
      httpMock.expectOne(isRead).flush(exams);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;
      const titles = () => Array.from(root.querySelectorAll('.card__title a')).map((a) => a.textContent?.trim());
      const type = (text: string) => {
        const box = root.querySelector<HTMLInputElement>('input[type="search"]')!;
        box.value = text;
        box.dispatchEvent(new Event('input'));
        fixture.detectChanges();
      };
      const choose = (status: string) => {
        const select = root.querySelector<HTMLSelectElement>('#filter-exam-status')!;
        select.value = status;
        select.dispatchEvent(new Event('change'));
        fixture.detectChanges();
      };
      return { fixture, root, titles, type, choose };
    }

    it('narrows the list by what is typed, in the name or the description, and says how many of how many', () => {
      const { root, titles, type } = open([exam('1', 'Maths Final'), exam('2', 'Physics Mock', 'Draft', 'covers algebra too'), exam('3', 'English')]);
      expect(root.querySelector('.list-toolbar__count')).toBeNull();

      type('maths');
      expect(titles()).toEqual(['Maths Final']);
      expect(root.querySelector('.list-toolbar__count')?.textContent).toBe('Showing 1 of 1');

      type('ALGEBRA');
      expect(titles()).toEqual(['Physics Mock']);

      type('');
      expect(titles()).toEqual(['Maths Final', 'Physics Mock', 'English']);
    });

    it('narrows the list by status, shows it as a chip, and brings the list back when the chip is removed', () => {
      const { fixture, root, titles, choose } = open([exam('1', 'Maths Final', 'Published'), exam('2', 'Physics Mock', 'Draft')]);

      choose('Published');
      expect(titles()).toEqual(['Maths Final']);
      expect(root.querySelector('.list-chip')?.textContent).toContain('Status: Published');

      root.querySelector<HTMLButtonElement>('.list-chip button')!.click();
      fixture.detectChanges();
      expect(titles()).toEqual(['Maths Final', 'Physics Mock']);
      expect(root.querySelector('.list-chip')).toBeNull();
    });

    it('says nothing matches, and keeps the strip so the search can be changed', () => {
      const { root, titles, type } = open([exam('1', 'Maths Final')]);

      type('zzz');

      expect(titles()).toEqual([]);
      expect(root.textContent).toContain('Nothing matches');
      expect(root.querySelector('input[type="search"]')).not.toBeNull();
    });

    it('draws twenty exams at a time and loads the next twenty on request', () => {
      const many = Array.from({ length: 45 }, (_, i) => exam(String(i), `Exam ${i}`));
      const { fixture, root, titles } = open(many);
      expect(titles().length).toBe(20);
      expect(root.querySelector('.list-toolbar__count')?.textContent).toBe('Showing 20 of 45');

      root.querySelector<HTMLButtonElement>('app-load-more button')!.click();
      fixture.detectChanges();
      expect(titles().length).toBe(40);

      root.querySelector<HTMLButtonElement>('app-load-more button')!.click();
      fixture.detectChanges();
      expect(titles().length).toBe(45);
      expect(root.querySelector('app-load-more button')).toBeNull();
    });

    it('starts from a search and a status carried in the address', async () => {
      TestBed.inject(Router).resetConfig([{ path: 'exams', component: ExamList }]);
      const harness = await RouterTestingHarness.create();
      const page = harness.navigateByUrl('/exams?q=final&status=Published', ExamList);
      const component = await page;
      httpMock.expectOne(isRead).flush([exam('1', 'Maths Final', 'Published'), exam('2', 'Maths Final Draft', 'Draft'), exam('3', 'English', 'Published')]);
      harness.detectChanges();

      const titles = Array.from((harness.routeNativeElement as HTMLElement).querySelectorAll('.card__title a')).map((a) => a.textContent?.trim());
      expect(titles).toEqual(['Maths Final']);
      expect(component).toBeTruthy();
    });
  });
});
