import { ComponentFixture, TestBed } from '@angular/core/testing';
import { QuestionDto } from '../../question-bank/question.models';
import { DrawQuestionsRequest, ExamScopeType, ExamSectionDto } from '../exam.models';
import { ExamSectionCard } from './exam-section-card';

const section = (overrides: Partial<ExamSectionDto> = {}): ExamSectionDto => ({
  id: 's1',
  name: 'Algebra',
  timeSeconds: null,
  order: 2,
  questions: [
    { id: 'eq1', questionId: 'q1', order: 1, text: '<p>What is 2 + 2?</p>' },
    { id: 'eq2', questionId: 'q2', order: 2, text: null },
  ],
  ...overrides,
});

const bankQuestion = (id: string, text: string): QuestionDto => ({
  id, text, createdBy: 'u1', createdAtUtc: '2026-10-02T00:00:00Z',
  chapterId: null, chapterTitle: null, bookId: null, bookName: null,
  usage: { examCount: 0, examNames: [], answered: false },
  difficulty: null,
  topics: [],
  allowsMultiple: false,
  options: [{ id: `${id}-a`, text: 'A', isCorrect: true, isPinned: false }, { id: `${id}-b`, text: 'B', isCorrect: false, isPinned: false }],
});

describe('ExamSectionCard', () => {
  let fixture: ComponentFixture<ExamSectionCard>;
  let root: HTMLElement;
  let renamed: { sectionId: string; name: string }[];
  let removed: string[];
  let added: { sectionId: string; questionId: string }[];
  let takenOut: { sectionId: string; questionId: string }[];
  let drawn: { sectionId: string; request: DrawQuestionsRequest }[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ExamSectionCard] }).compileComponents();
  });

  function show(
    s: ExamSectionDto = section(),
    inputs: { editable?: boolean; busy?: boolean; choices?: QuestionDto[]; scope?: ExamScopeType; topics?: string[] } = {},
  ) {
    fixture = TestBed.createComponent(ExamSectionCard);
    fixture.componentRef.setInput('section', s);
    fixture.componentRef.setInput('editable', inputs.editable ?? true);
    if (inputs.busy !== undefined) fixture.componentRef.setInput('busy', inputs.busy);
    if (inputs.choices !== undefined) fixture.componentRef.setInput('choices', inputs.choices);
    if (inputs.scope !== undefined) fixture.componentRef.setInput('scope', inputs.scope);
    if (inputs.topics !== undefined) fixture.componentRef.setInput('topics', inputs.topics);
    renamed = [];
    removed = [];
    added = [];
    takenOut = [];
    drawn = [];
    fixture.componentInstance.drawRequested.subscribe((r) => drawn.push(r));
    fixture.componentInstance.renameRequested.subscribe((r) => renamed.push(r));
    fixture.componentInstance.removeConfirmed.subscribe((id) => removed.push(id));
    fixture.componentInstance.addRequested.subscribe((r) => added.push(r));
    fixture.componentInstance.takeOutRequested.subscribe((r) => takenOut.push(r));
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const button = (label: string) => Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;
  const press = (label: string) => {
    button(label).click();
    fixture.detectChanges();
  };
  const nameField = () => root.querySelector('input[aria-label="Section name"]') as HTMLInputElement;
  const typeName = (value: string) => {
    nameField().value = value;
    nameField().dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };

  it('shows the section number and name, and its questions as one line of text each', () => {
    show();

    expect(root.querySelector('h3')?.textContent?.trim()).toBe('2. Algebra');
    const items = Array.from(root.querySelectorAll('li')).map((li) => li.textContent?.replace(/\s+/g, ' ').trim());
    expect(items[0]).toContain('What is 2 + 2?');
    expect(items[1]).toContain('(question no longer in the bank)');
  });

  it('says there are no questions yet', () => {
    show(section({ questions: [] }));

    expect(root.textContent).toContain('No questions yet.');
  });

  it('is read-only when the exam is not a draft: no picker, no Remove, no Rename', () => {
    show(section(), { editable: false });

    expect(root.querySelector('select')).toBeNull();
    expect(button('Remove')).toBeUndefined();
    expect(button('Rename')).toBeUndefined();
    expect(button('Remove section')).toBeUndefined();
    expect(root.querySelectorAll('li').length).toBe(2);
  });

  describe('taking a question out', () => {
    it('has a Remove for each question, named for a screen reader, that asks the page to take it out', () => {
      show();

      const buttons = Array.from(root.querySelectorAll('li button')) as HTMLButtonElement[];
      expect(buttons.map((b) => b.textContent?.trim())).toEqual(['Remove', 'Remove']);
      expect(buttons[0].getAttribute('aria-label')).toBe('Remove What is 2 + 2? from the exam');
      expect(buttons[1].getAttribute('aria-label')).toBe('Remove a question that is no longer in the bank from the exam');

      buttons[0].click();

      expect(takenOut).toEqual([{ sectionId: 's1', questionId: 'q1' }]);
    });

    it('cannot be pressed while a request is running', () => {
      show(section(), { busy: true });

      expect((root.querySelector('li button') as HTMLButtonElement).disabled).toBe(true);
    });
  });

  describe('adding a question', () => {
    it('offers the questions it is given and sends the chosen one', () => {
      show(section(), { choices: [bankQuestion('q9', 'Capital of France?')] });
      const select = root.querySelector('select') as HTMLSelectElement;
      expect(Array.from(select.options).map((o) => o.textContent?.trim())).toEqual(['Choose a question…', 'Capital of France?']);

      select.value = 'q9';
      select.dispatchEvent(new Event('change'));
      press('Add question');

      expect(added).toEqual([{ sectionId: 's1', questionId: 'q9' }]);
    });

    it('sends an empty choice too, so the page can say that nothing was chosen', () => {
      show();

      press('Add question');

      expect(added).toEqual([{ sectionId: 's1', questionId: '' }]);
    });

    it('says what the exam is limited to, and what to do when nothing is left to add', () => {
      show(section(), { scope: 'Chapters', choices: [] });

      expect(root.textContent).toContain("Only questions from this exam's chapters are offered.");
      expect(root.textContent).toContain('There are none left to add');
    });

    it('says nothing about a limit for an exam without one', () => {
      show(section(), { scope: 'Independent' });

      expect(root.textContent).not.toContain('are offered');
    });
  });

  describe('renaming', () => {
    it('opens a name field with the current name and sends the new one', () => {
      show();

      press('Rename');
      expect(root.querySelector('h3')).toBeNull();
      expect(nameField().value).toBe('Algebra');

      typeName('  Geometry ');
      press('Save name');

      expect(renamed).toEqual([{ sectionId: 's1', name: 'Geometry' }]);
    });

    it('sends nothing when the name is blank or unchanged', () => {
      show();
      press('Rename');

      typeName('   ');
      expect(button('Save name').disabled).toBe(true);
      typeName('Algebra');
      (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

      expect(renamed).toEqual([]);
    });

    it('can be backed out of with nothing sent', () => {
      show();
      press('Rename');

      press('Cancel');

      expect(root.querySelector('h3')).not.toBeNull();
      expect(renamed).toEqual([]);
    });

    it('closes once the section carries its new name, and stays open when the request was refused', () => {
      show();
      press('Rename');
      typeName('Geometry');

      fixture.componentRef.setInput('busy', true);
      fixture.componentRef.setInput('busy', false);
      fixture.detectChanges();
      expect(nameField()).not.toBeNull(); // nothing changed, so the author's typing is kept

      fixture.componentRef.setInput('section', section({ name: 'Geometry' }));
      fixture.detectChanges();
      expect(root.querySelector('input[aria-label="Section name"]')).toBeNull();
      expect(root.querySelector('h3')?.textContent?.trim()).toBe('2. Geometry');
    });
  });

  describe('removing the section', () => {
    it('asks first, naming how many questions go with it, and removes only once confirmed', () => {
      show();

      press('Remove section');
      const dialog = root.querySelector('[role="alertdialog"]');
      expect(dialog?.textContent).toContain('Remove this section and its 2 questions from the exam?');
      expect(dialog?.textContent).toContain('They stay in the question bank.');
      expect(removed).toEqual([]);

      press('Remove section'); // the confirming one
      expect(removed).toEqual(['s1']);
    });

    it('goes back to normal when cancelled', () => {
      show();
      press('Remove section');

      press('Cancel');

      expect(root.querySelector('[role="alertdialog"]')).toBeNull();
      expect(removed).toEqual([]);
    });

    it('does not mention questions for an empty section', () => {
      show(section({ questions: [] }));

      press('Remove section');

      expect(root.querySelector('[role="alertdialog"]')?.textContent).toContain('Remove this section from the exam?');
    });
  });

  describe('drawing random questions', () => {
    const field = (label: string) => Array.from(root.querySelectorAll('label')).find((l) => l.textContent?.trim().startsWith(label))?.querySelector('input, select') as HTMLInputElement & HTMLSelectElement;
    const set = (label: string, value: string) => {
      field(label).value = value;
      field(label).dispatchEvent(new Event(field(label).tagName === 'SELECT' ? 'change' : 'input'));
      fixture.detectChanges();
    };

    it('keeps the draw row closed until asked for, and offers it only while editable', () => {
      show();
      expect(root.querySelector('.section-card__draw form')).toBeNull();

      press('Add random questions…');
      expect(root.querySelector('.section-card__draw form')).not.toBeNull();

      show(section(), { editable: false });
      expect(button('Add random questions…')).toBeUndefined();
    });

    it('asks for the count, difficulty and topic chosen, for this section', () => {
      show(section(), { topics: ['algebra', 'fractions'] });
      press('Add random questions…');

      set('How many', '3');
      set('Difficulty', 'hard');
      set('Topic', 'fractions');
      press('Draw');

      expect(drawn).toEqual([{ sectionId: 's1', request: { count: 3, difficulty: 'hard', topic: 'fractions' } }]);
    });

    it('asks for a rule instead of a draw when the author wants it redrawn for each candidate', () => {
      show();
      const rules: { sectionId: string; request: DrawQuestionsRequest }[] = [];
      fixture.componentInstance.drawRuleRequested.subscribe((r) => rules.push(r));
      press('Add random questions…');

      field('Draw again for each candidate').click();
      fixture.detectChanges();
      press('Add rule');

      expect(rules).toEqual([{ sectionId: 's1', request: { count: 5, difficulty: null, topic: null } }]);
      expect(drawn).toEqual([]);
    });

    it('lists the section\'s rules and lets the author take one out', () => {
      show(section({ drawRules: [{ id: 'r1', order: 1, count: 3, bookId: null, chapterId: null, difficulty: 'easy', topic: 'algebra' }] }));
      const removedRules: { sectionId: string; ruleId: string }[] = [];
      fixture.componentInstance.drawRuleRemoveRequested.subscribe((r) => removedRules.push(r));

      expect(root.textContent).toContain('3 easy questions on algebra');
      press('Remove rule');

      expect(removedRules).toEqual([{ sectionId: 's1', ruleId: 'r1' }]);
    });

    it('sends null for "Any", and starts at five questions', () => {
      show();
      press('Add random questions…');

      press('Draw');

      expect(drawn).toEqual([{ sectionId: 's1', request: { count: 5, difficulty: null, topic: null } }]);
    });

    it('lists the topics in use', () => {
      show(section(), { topics: ['algebra', 'fractions'] });
      press('Add random questions…');

      const options = Array.from(field('Topic').querySelectorAll('option')).map((o) => o.textContent?.trim());
      expect(options).toEqual(['Any', 'algebra', 'fractions']);
    });

    it('refuses a count outside 1 to 100 without sending anything', () => {
      show();
      press('Add random questions…');

      set('How many', '0');
      expect(button('Draw').disabled).toBe(true);
      expect(root.textContent).toContain('Draw between 1 and 100 questions.');
      set('How many', '101');
      expect(button('Draw').disabled).toBe(true);
      expect(drawn).toEqual([]);
    });

    it('cannot be pressed while a request is running', () => {
      show(section(), { busy: true });

      expect(button('Add random questions…').disabled).toBe(true);
    });
  });
});
