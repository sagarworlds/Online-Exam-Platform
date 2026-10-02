import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { QuestionBank } from './question-bank';

// The base URL differs between builds and the test environment, so requests are matched by their path.
const isList = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/questions');

describe('QuestionBank', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuestionBank],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function create() {
    const fixture = TestBed.createComponent(QuestionBank);
    fixture.detectChanges();
    return fixture;
  }

  function fill(root: HTMLElement, text: string, options: string[]): void {
    const set = (el: HTMLInputElement | HTMLTextAreaElement, value: string) => {
      el.value = value;
      el.dispatchEvent(new Event('input'));
    };
    set(root.querySelector('#question-text') as HTMLTextAreaElement, text);
    const inputs = root.querySelectorAll<HTMLInputElement>('input[type="text"]');
    options.forEach((value, i) => set(inputs[i], value));
  }

  it('lists the newest questions and marks the correct option', () => {
    const fixture = create();
    httpMock.expectOne(isList).flush([
      {
        id: 'q1',
        text: 'What is 2 + 2?',
        options: [
          { id: 'o1', text: '3', isCorrect: false },
          { id: 'o2', text: '4', isCorrect: true },
        ],
        createdBy: 'u1',
        createdAtUtc: '2026-10-02T00:00:00Z',
      },
    ]);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('What is 2 + 2?');
    expect(text).toContain('correct');
  });

  it('shows the formatting of a question in the list, and nothing executable', () => {
    const w = window as unknown as { __ran?: boolean };
    const fixture = create();
    httpMock.expectOne(isList).flush([
      {
        id: 'q1',
        text: '<p>Water is H<sub>2</sub>O</p><img src="x" onerror="window.__ran = true">',
        options: [
          { id: 'o1', text: 'Yes', isCorrect: true },
          { id: 'o2', text: 'No', isCorrect: false },
        ],
        createdBy: 'u1',
        createdAtUtc: '2026-10-02T00:00:00Z',
      },
    ]);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.question-card sub')?.textContent).toBe('2');
    expect(root.querySelector('[onerror]')).toBeNull();
    expect(w.__ran).toBeUndefined();
  });

  it('keeps Save disabled until every option has text and a correct option is chosen', () => {
    const fixture = create();
    httpMock.expectOne(isList).flush([]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const save = root.querySelector('button.primary') as HTMLButtonElement;

    fill(root, 'Q?', ['A', 'B']);
    fixture.detectChanges();
    expect(save.disabled).toBe(true);

    (root.querySelectorAll('input[type="radio"]')[1] as HTMLInputElement).dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(save.disabled).toBe(false);
  });

  it('posts the options with the chosen one marked correct, then reloads the list', () => {
    const fixture = create();
    httpMock.expectOne(isList).flush([]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    fill(root, 'Capital of France?', ['Rome', 'Paris']);
    (root.querySelectorAll('input[type="radio"]')[1] as HTMLInputElement).dispatchEvent(new Event('change'));
    fixture.detectChanges();
    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const post = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith('/v1/questions'));
    expect(post.request.body).toEqual({
      text: 'Capital of France?',
      options: [
        { text: 'Rome', isCorrect: false },
        { text: 'Paris', isCorrect: true },
      ],
    });
    post.flush({ id: 'q9' }, { status: 201, statusText: 'Created' });
    httpMock.expectOne(isList).flush([]);
    fixture.detectChanges();
    expect(root.textContent).toContain('Question saved.');
  });

  it('adds options up to six and removes down to two', () => {
    const fixture = create();
    httpMock.expectOne(isList).flush([]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const count = () => root.querySelectorAll('input[type="text"]').length;
    const addButton = () =>
      Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Add option') as HTMLButtonElement;

    expect(count()).toBe(2);
    for (let i = 0; i < 6; i++) {
      addButton().click();
      fixture.detectChanges();
    }
    expect(count()).toBe(6);
    expect(addButton().disabled).toBe(true);

    for (let i = 0; i < 6; i++) {
      (Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Remove') as HTMLButtonElement).click();
      fixture.detectChanges();
    }
    expect(count()).toBe(2);
  });

  it('shows the API error when saving fails', () => {
    const fixture = create();
    httpMock.expectOne(isList).flush([]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    fill(root, 'Q?', ['A', 'B']);
    (root.querySelectorAll('input[type="radio"]')[0] as HTMLInputElement).dispatchEvent(new Event('change'));
    fixture.detectChanges();
    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    httpMock
      .expectOne((r) => r.method === 'POST')
      .flush({ title: 'invalid_question', detail: 'Exactly one option must be marked correct.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(root.textContent).toContain('Exactly one option must be marked correct.');
  });
});
