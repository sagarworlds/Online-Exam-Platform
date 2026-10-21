import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { BatchCreate } from './batch-create';

describe('BatchCreate', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BatchCreate],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  const isCreate = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/batches');
  const isExams = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/exams');
  const exam = (id: string, name: string, status = 'Published') => ({ id, name, status });

  /** Opens the page and answers the read of the exams with `exams`, or fails it. */
  function open(exams: unknown[] | 'fail' = [exam('e1', 'Maths Final')]) {
    // The list is another page; going to it is only noted here.
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(BatchCreate);
    fixture.detectChanges();
    if (exams === 'fail') {
      httpMock.expectOne(isExams).flush({}, { status: 500, statusText: 'Server Error' });
    } else {
      httpMock.expectOne(isExams).flush(exams);
    }
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const field = (label: string) =>
      Array.from(root.querySelectorAll('label')).find((l) => l.textContent?.includes(label))!.control as HTMLInputElement & HTMLSelectElement;
    const fill = (label: string, value: string) => {
      const control = field(label);
      control.value = value;
      control.dispatchEvent(new Event(control.tagName === 'SELECT' ? 'change' : 'input'));
    };
    const submit = () => {
      root.querySelector('form')!.dispatchEvent(new Event('submit'));
      fixture.detectChanges();
    };
    return { fixture, root, field, fill, submit, navigate };
  }

  it('offers Create from the start, and does not switch it off with no reason given', () => {
    const { root } = open();

    const create = root.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(create.disabled).toBe(false);
    expect(root.querySelector('.field-error')).toBeNull();
  });

  it('says which fields are wrong when Create is pressed, ties each message to its field, and goes to the first', async () => {
    const { fixture, root, field, submit } = open();
    document.body.appendChild(root);

    submit();
    await Promise.resolve();
    fixture.detectChanges();
    fixture.detectChanges();

    const messages = Array.from(root.querySelectorAll('.field-error')).map((e) => e.textContent?.trim());
    expect(messages).toEqual(['Choose the exam this batch sits for.', 'Enter a batch name.']);
    expect(field('Exam').getAttribute('aria-invalid')).toBe('true');
    expect(field('Batch name').getAttribute('aria-invalid')).toBe('true');
    expect(field('Maximum members').hasAttribute('aria-invalid')).toBe(false);
    expect(field('Exam').getAttribute('aria-describedby')).toContain(root.querySelector('.field-error')!.id);
    expect(document.activeElement).toBe(field('Exam'));
    httpMock.expectNone(isCreate);
    root.remove();
  });

  it('marks required fields, and the optional one in words', () => {
    const { root } = open();

    const labels = Array.from(root.querySelectorAll('label')).map((l) => l.textContent?.replace(/\s+/g, ' ').trim());
    expect(labels).toEqual(['Exam *', 'Batch name *', 'Description (optional)', 'Maximum members *']);
  });

  it('asks for at least one member', () => {
    const { fill, submit, root } = open();
    fill('Exam', 'e1');
    fill('Batch name', 'Morning');
    fill('Maximum members', '0');
    submit();

    expect(root.querySelector('.field-error')?.textContent).toContain('1 or more');
    httpMock.expectNone(isCreate);
  });

  describe('choosing the exam', () => {
    it('offers the exams by name, with whether each is a draft or published, and not the archived ones', () => {
      const { field } = open([exam('e2', 'Physics', 'Draft'), exam('e1', 'Maths Final'), exam('e3', 'Old paper', 'Archived')]);

      const options = Array.from(field('Exam').options).map((o) => [o.value, o.textContent?.trim()]);
      expect(options).toEqual([
        ['', 'Choose an exam…'],
        ['e1', 'Maths Final (Published)'],
        ['e2', 'Physics (Draft)'],
      ]);
    });

    it('says only the newest 200 exams are listed when the list is full, and says nothing when it is not', () => {
      const full = open(Array.from({ length: 200 }, (_, i) => exam(`e${i}`, `Exam ${i}`)));
      expect(full.root.textContent).toContain('Only the newest 200 exams are listed.');

    });

    it('says there are no exams, and where to make one, rather than showing an empty list', () => {
      const { root } = open([]);

      expect(root.textContent).toContain('There are no exams to choose from yet.');
      expect(root.querySelector('a[href="/exams/create"]')).not.toBeNull();
    });

    it('says the exams could not be loaded and offers to try again, which fills the list', () => {
      const { fixture, root, field } = open('fail');

      expect(root.querySelector('[role="alert"]')?.textContent).toContain('The exams could not be loaded');
      (root.querySelector('.warning-message button') as HTMLButtonElement).click();
      httpMock.expectOne(isExams).flush([exam('e1', 'Maths Final')]);
      fixture.detectChanges();

      expect(root.querySelector('.warning-message')).toBeNull();
      expect(field('Exam').options.length).toBe(2);
    });
  });

  it('creates the batch for the exam chosen and goes back to the list', () => {
    const { fill, submit, navigate } = open();
    fill('Exam', 'e1');
    fill('Batch name', 'Morning');
    fill('Maximum members', '30');

    submit();
    const request = httpMock.expectOne(isCreate);
    expect(request.request.body).toMatchObject({ examId: 'e1', name: 'Morning', maxMembers: 30 });
    request.flush({ id: 'b1' });

    expect(navigate).toHaveBeenCalledWith(['/batches']);
  });

  it('shows the reason the server gives when the batch cannot be created, and stops being busy', () => {
    const { fixture, root, fill, submit } = open();
    fill('Exam', 'e1');
    fill('Batch name', 'Morning');

    submit();
    expect(root.querySelector('button[type="submit"]')?.getAttribute('aria-busy')).toBe('true');
    httpMock.expectOne(isCreate).flush({ detail: 'That exam does not exist.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('That exam does not exist.');
    expect(root.querySelector('button[type="submit"]')?.getAttribute('aria-busy')).toBe('false');
  });

  it('does not send a second request while the first is running', () => {
    const { fill, submit } = open();
    fill('Exam', 'e1');
    fill('Batch name', 'Morning');

    submit();
    submit();

    httpMock.expectOne(isCreate).flush({ id: 'b1' });
  });
});
