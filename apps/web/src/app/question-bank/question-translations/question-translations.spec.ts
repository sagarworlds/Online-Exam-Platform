import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { QuestionDto, QuestionTranslation } from '../question.models';
import { QuestionTranslations } from './question-translations';

const isTranslations = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/questions/q1/translations');
const isAdd = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/questions/q1/translations');

const question: QuestionDto = {
  id: 'q1', text: '<p>Capital of France?</p>', createdBy: 'u1', createdAtUtc: '2026-10-08T00:00:00Z',
  chapterId: null, chapterTitle: null, bookId: null, bookName: null,
  usage: { examCount: 0, examNames: [], answered: false },
  difficulty: null, topics: [], allowsMultiple: false, language: 'en',
  options: [
    { id: 'o1', text: 'Paris', isCorrect: true, isPinned: false },
    { id: 'o2', text: 'Rome', isCorrect: false, isPinned: false },
  ],
};
const english: QuestionTranslation = { id: 'q1', language: 'en', preview: 'Capital of France?', status: 'draft' };
const hindi: QuestionTranslation = { id: 'q2', language: 'hi', preview: 'फ्रांस की राजधानी?', status: 'approved' };

describe('QuestionTranslations', () => {
  let fixture: ComponentFixture<QuestionTranslations>;
  let root: HTMLElement;
  let httpMock: HttpTestingController;
  let added: number;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuestionTranslations],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function show(linked: QuestionTranslation[]) {
    fixture = TestBed.createComponent(QuestionTranslations);
    fixture.componentRef.setInput('question', question);
    added = 0;
    fixture.componentInstance.added.subscribe(() => added++);
    fixture.detectChanges();
    httpMock.expectOne(isTranslations).flush(linked);
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const buttons = () => Array.from(root.querySelectorAll('button')).map((b) => b.textContent?.trim());
  const press = (label: string) => {
    (Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement).click();
    fixture.detectChanges();
  };
  const type = (selector: string, index: number, value: string) => {
    const input = root.querySelectorAll<HTMLInputElement>(selector)[index];
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };
  const fillForm = (text: string, options: string[]) => {
    (fixture.componentInstance as unknown as { form: { controls: { text: { setValue(v: string): void } } } }).form.controls.text.setValue(text);
    options.forEach((value, i) => type('.option-row input[type="text"]', i, value));
    fixture.detectChanges();
  };

  it('lists the question and its translations, and offers each language the group lacks', () => {
    show([english, hindi]);

    expect(root.textContent).toContain('English');
    expect(root.textContent).toContain('This question');
    expect(root.textContent).toContain('Hindi');
    expect(root.querySelector('a')?.textContent).toContain('फ्रांस की राजधानी?');
    expect(root.querySelector('a')?.getAttribute('href')).toBe('/admin/questions/q2/edit');
    expect(root.textContent).toContain('Approved');
    expect(buttons()).toEqual(['Add Marathi translation']);
  });

  it('offers Hindi and Marathi when the question has no translation yet', () => {
    show([english]);

    expect(buttons()).toEqual(['Add Hindi translation', 'Add Marathi translation']);
  });

  it('asks for one box per option of the question, showing the source option and which is correct', () => {
    show([english]);

    press('Add Hindi translation');

    const hints = Array.from(root.querySelectorAll('.option-row .hint')).map((h) => h.textContent?.replace(/\s+/g, ' ').trim());
    expect(hints).toEqual(['Paris (correct)', 'Rome']);
    expect(root.querySelectorAll('.option-row input[type="text"]').length).toBe(2);
    expect(root.textContent).toContain('copied from this question');
  });

  it('cannot be saved until every box is filled', () => {
    show([english]);
    press('Add Hindi translation');
    const save = () => Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Save translation') as HTMLButtonElement;

    fillForm('<p>फ्रांस की राजधानी?</p>', ['पेरिस', '']);
    expect(save().disabled).toBe(true);

    fillForm('<p>फ्रांस की राजधानी?</p>', ['पेरिस', 'रोम']);
    expect(save().disabled).toBe(false);
  });

  it('sends the language, the text and the options in order, then reloads the list and says a translation was added', () => {
    show([english]);
    press('Add Marathi translation');
    fillForm('<p>फ्रान्सची राजधानी?</p>', ['पॅरिस', 'रोम']);

    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    const post = httpMock.expectOne(isAdd);
    expect(post.request.body).toEqual({ language: 'mr', text: '<p>फ्रान्सची राजधानी?</p>', options: ['पॅरिस', 'रोम'] });
    post.flush({ id: 'q3' }, { status: 201, statusText: 'Created' });
    httpMock.expectOne(isTranslations).flush([english, { id: 'q3', language: 'mr', preview: 'फ्रान्सची राजधानी?', status: 'draft' }]);
    fixture.detectChanges();

    expect(added).toBe(1);
    expect(root.querySelector('form')).toBeNull();
    expect(root.textContent).toContain('Marathi');
    expect(buttons()).toEqual(['Add Hindi translation']);
  });

  it('shows why a translation was refused and keeps what was typed', () => {
    show([english]);
    press('Add Hindi translation');
    fillForm('<p>x</p>', ['क', 'ख']);

    (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    httpMock
      .expectOne(isAdd)
      .flush({ title: 'translation_exists', detail: "This question already has a translation in 'hi'." }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(root.querySelector('.error-message')?.textContent).toContain('already has a translation');
    expect(root.querySelector('form')).not.toBeNull();
    expect(added).toBe(0);
  });

  it('closes the form without sending anything when cancelled', () => {
    show([english]);
    press('Add Hindi translation');

    press('Cancel');

    expect(root.querySelector('form')).toBeNull();
    httpMock.expectNone(isAdd);
  });

  it('says why the list could not be loaded', () => {
    fixture = TestBed.createComponent(QuestionTranslations);
    fixture.componentRef.setInput('question', question);
    fixture.detectChanges();
    httpMock.expectOne(isTranslations).flush({ title: 'forbidden', detail: 'No access.' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.error-message')?.textContent).toContain('No access.');
  });
});
