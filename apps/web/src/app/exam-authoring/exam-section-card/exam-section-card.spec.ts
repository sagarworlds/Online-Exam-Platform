import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DrawQuestionsRequest, ExamScopeDto, ExamSectionDto } from '../exam.models';
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

describe('ExamSectionCard', () => {
  let fixture: ComponentFixture<ExamSectionCard>;
  let root: HTMLElement;
  let renamed: { sectionId: string; name: string }[];
  let removed: string[];
  let added: { sectionId: string; questionId: string }[];
  let takenOut: { sectionId: string; questionId: string }[];
  let drawn: { sectionId: string; request: DrawQuestionsRequest }[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ExamSectionCard], providers: [provideHttpClient(), provideHttpClientTesting()] }).compileComponents();
  });

  function show(
    s: ExamSectionDto = section(),
    inputs: { editable?: boolean; busy?: boolean; examScope?: ExamScopeDto; excluded?: ReadonlySet<string>; topics?: string[] } = {},
  ) {
    fixture = TestBed.createComponent(ExamSectionCard);
    fixture.componentRef.setInput('section', s);
    fixture.componentRef.setInput('editable', inputs.editable ?? true);
    if (inputs.busy !== undefined) fixture.componentRef.setInput('busy', inputs.busy);
    if (inputs.examScope !== undefined) fixture.componentRef.setInput('examScope', inputs.examScope);
    if (inputs.excluded !== undefined) fixture.componentRef.setInput('excluded', inputs.excluded);
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
  /** The trash icon with this name; an icon has no text, so it is found by its label. */
  const icon = (label: string) => root.querySelector(`button[aria-label="${label}"]`) as HTMLButtonElement;
  const pressIcon = (label: string) => {
    icon(label).click();
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

  it('is read-only when the exam is not a draft: no picker, no trash icons, no Rename', () => {
    show(section(), { editable: false });

    expect(root.querySelector('select')).toBeNull();
    expect(root.querySelector('app-confirm-remove')).toBeNull();
    expect(button('Rename')).toBeUndefined();
    expect(root.querySelectorAll('li').length).toBe(2);
  });

  describe('taking a question out', () => {
    it('has a trash icon for each question, named for it, that asks before taking it out', () => {
      show();

      const icons = Array.from(root.querySelectorAll('li app-confirm-remove button')) as HTMLButtonElement[];
      expect(icons.map((b) => b.textContent?.trim())).toEqual(['', '']);
      expect(icons[0].getAttribute('aria-label')).toBe('Remove What is 2 + 2? from the exam');
      expect(icons[1].getAttribute('aria-label')).toBe('Remove a question that is no longer in the bank from the exam');
      expect(icons[0].querySelector('svg')).not.toBeNull();

      icons[0].click();
      fixture.detectChanges();

      expect(root.querySelector('li [role="alertdialog"]')?.textContent).toContain('Remove this question from the exam? It stays in the question bank.');
      expect(takenOut).toEqual([]);
    });

    it('takes the question out only once confirmed', () => {
      show();
      pressIcon('Remove What is 2 + 2? from the exam');

      press('Remove');

      expect(takenOut).toEqual([{ sectionId: 's1', questionId: 'q1' }]);
    });

    it('takes nothing out when cancelled, and goes back to the icon', () => {
      show();
      pressIcon('Remove What is 2 + 2? from the exam');

      press('Cancel');

      expect(takenOut).toEqual([]);
      expect(root.querySelector('[role="alertdialog"]')).toBeNull();
      expect(icon('Remove What is 2 + 2? from the exam')).not.toBeNull();
    });

    it('cannot be pressed while a request is running', () => {
      show(section(), { busy: true });

      expect((root.querySelector('li app-confirm-remove button') as HTMLButtonElement).disabled).toBe(true);
    });
  });

  describe('moving', () => {
    const questionsOf = () => [
      { id: 'eq1', questionId: 'q1', order: 1, text: '<p>First?</p>' },
      { id: 'eq2', questionId: 'q2', order: 2, text: '<p>Second?</p>' },
      { id: 'eq3', questionId: 'q3', order: 3, text: '<p>Third?</p>' },
    ];
    const moveQuestionButton = (id: string, direction: 'up' | 'down') =>
      root.querySelector<HTMLButtonElement>(`[data-question="${id}"][data-move="${direction}"]`)!;
    const moveSectionButton = (direction: 'up' | 'down') => root.querySelector<HTMLButtonElement>(`[data-section-move="${direction}"]`)!;
    let movedQuestions: { sectionId: string; questionId: string; direction: string }[];
    let movedSections: { sectionId: string; direction: string }[];

    function showWith(inputs: { index?: number; count?: number; busy?: boolean; editable?: boolean; questions?: ExamSectionDto['questions'] } = {}) {
      show(section({ questions: inputs.questions ?? questionsOf() }), { editable: inputs.editable, busy: inputs.busy });
      movedQuestions = [];
      movedSections = [];
      fixture.componentInstance.moveQuestionRequested.subscribe((m) => movedQuestions.push(m));
      fixture.componentInstance.moveSectionRequested.subscribe((m) => movedSections.push(m));
      fixture.componentRef.setInput('sectionIndex', inputs.index ?? 0);
      fixture.componentRef.setInput('sectionCount', inputs.count ?? 1);
      fixture.detectChanges();
    }

    it('gives each question an arrow up and an arrow down, named for the question', () => {
      showWith();

      expect(moveQuestionButton('q2', 'up').getAttribute('aria-label')).toBe('Move Second? up');
      expect(moveQuestionButton('q2', 'down').getAttribute('aria-label')).toBe('Move Second? down');
    });

    it('asks to move a question the way the arrow points, naming the section and the question', () => {
      showWith();

      moveQuestionButton('q2', 'up').click();
      moveQuestionButton('q2', 'down').click();

      expect(movedQuestions).toEqual([
        { sectionId: 's1', questionId: 'q2', direction: 'up' },
        { sectionId: 's1', questionId: 'q2', direction: 'down' },
      ]);
    });

    it('dims the first question’s up arrow and the last one’s down arrow, and does nothing when they are pressed', () => {
      showWith();

      expect(moveQuestionButton('q1', 'up').getAttribute('aria-disabled')).toBe('true');
      expect(moveQuestionButton('q3', 'down').getAttribute('aria-disabled')).toBe('true');
      expect(moveQuestionButton('q1', 'down').hasAttribute('aria-disabled')).toBe(false);

      moveQuestionButton('q1', 'up').click();
      moveQuestionButton('q3', 'down').click();

      expect(movedQuestions).toEqual([]);
    });

    it('does not use the disabled attribute for a dimmed arrow, so it can still be reached to read why', () => {
      showWith();

      expect(moveQuestionButton('q1', 'up').disabled).toBe(false);
    });

    it('offers no arrows on a section with one question, since there is nowhere to go', () => {
      showWith({ questions: [questionsOf()[0]] });

      expect(root.querySelector('[data-move]')).toBeNull();
    });

    it('offers none while a request is running, and none on a section that cannot be changed', () => {
      showWith({ busy: true });
      expect(moveQuestionButton('q2', 'up').getAttribute('aria-disabled')).toBe('true');
      moveQuestionButton('q2', 'up').click();
      expect(movedQuestions).toEqual([]);

      showWith({ editable: false });
      expect(root.querySelector('[data-move]')).toBeNull();
      expect(root.querySelector('[data-section-move]')).toBeNull();
    });

    it('names the arrow of a question whose text is gone, rather than leaving it blank', () => {
      showWith({ questions: [{ id: 'eq1', questionId: 'q1', order: 1, text: null }, ...questionsOf().slice(1)] });

      expect(moveQuestionButton('q1', 'down').getAttribute('aria-label')).toContain('Move ');
      expect(moveQuestionButton('q1', 'down').getAttribute('aria-label')).not.toBe('Move  down');
    });

    it('has Move up and Move down for a section, in words, named for the section, when the exam has more than one', () => {
      showWith({ index: 1, count: 3 });

      expect(moveSectionButton('up').textContent?.trim()).toBe('Move up');
      expect(moveSectionButton('down').textContent?.trim()).toBe('Move down');
      expect(moveSectionButton('up').getAttribute('aria-label')).toBe('Move up: Algebra');
    });

    it('asks to move the section the way the button says', () => {
      showWith({ index: 1, count: 3 });

      moveSectionButton('up').click();
      moveSectionButton('down').click();

      expect(movedSections).toEqual([
        { sectionId: 's1', direction: 'up' },
        { sectionId: 's1', direction: 'down' },
      ]);
    });

    it('dims Move up on the first section and Move down on the last, and offers neither for the only section', () => {
      showWith({ index: 0, count: 2 });
      expect(moveSectionButton('up').getAttribute('aria-disabled')).toBe('true');
      expect(moveSectionButton('down').hasAttribute('aria-disabled')).toBe(false);
      moveSectionButton('up').click();
      expect(movedSections).toEqual([]);

      showWith({ index: 1, count: 2 });
      expect(moveSectionButton('down').getAttribute('aria-disabled')).toBe('true');

      showWith({ index: 0, count: 1 });
      expect(root.querySelector('[data-section-move]')).toBeNull();
    });

    describe('focus', () => {
      it('goes back to the same arrow once the page has read the exam again', async () => {
        showWith();
        document.body.appendChild(root);
        const arrow = moveQuestionButton('q3', 'up');
        arrow.focus();

        arrow.click();
        fixture.componentRef.setInput('busy', true);
        fixture.detectChanges();
        // The page reads the exam again: the question is in its new place, so its row is drawn afresh.
        fixture.componentRef.setInput('section', section({ questions: [questionsOf()[0], questionsOf()[2], questionsOf()[1]].map((q, i) => ({ ...q, order: i + 1 })) }));
        fixture.componentRef.setInput('busy', false);
        fixture.detectChanges();
        await fixture.whenStable();

        // Second now, so it can still go up: focus is on the same arrow.
        expect(document.activeElement).toBe(moveQuestionButton('q3', 'up'));
        root.remove();
      });

      it('goes to the other arrow when the moved question has reached the end the first one points to', async () => {
        showWith();
        document.body.appendChild(root);
        const arrow = moveQuestionButton('q2', 'up');
        arrow.focus();

        arrow.click();
        fixture.componentRef.setInput('busy', true);
        fixture.componentRef.setInput('section', section({ questions: [questionsOf()[1], questionsOf()[0], questionsOf()[2]].map((q, i) => ({ ...q, order: i + 1 })) }));
        fixture.componentRef.setInput('busy', false);
        fixture.detectChanges();
        await fixture.whenStable();

        // Now first, so its up arrow is dimmed: focus stays on the pair, on the arrow that still works.
        expect(moveQuestionButton('q2', 'up').getAttribute('aria-disabled')).toBe('true');
        expect(document.activeElement).toBe(moveQuestionButton('q2', 'down'));
        root.remove();
      });

      it('goes back to the section’s button after a section was moved', async () => {
        showWith({ index: 1, count: 3 });
        document.body.appendChild(root);
        const button = moveSectionButton('up');
        button.focus();

        button.click();
        fixture.componentRef.setInput('busy', true);
        fixture.componentRef.setInput('sectionIndex', 0);
        fixture.componentRef.setInput('busy', false);
        fixture.detectChanges();
        await fixture.whenStable();

        expect(document.activeElement).toBe(moveSectionButton('down'));
        root.remove();
      });
    });
  });

  describe('adding a question', () => {
    const picker = () => root.querySelector('app-question-picker');
    const pickerRow = () => Array.from(root.querySelectorAll<HTMLDetailsElement>('details')).find((d) => d.textContent?.includes('Add a question from the bank'))!;

    function openPicker() {
      const details = pickerRow();
      details.open = true;
      details.dispatchEvent(new Event('toggle'));
      fixture.detectChanges();
    }

    it('has a row that opens the question picker, shut to begin with, and does not make the picker until it is opened', () => {
      show();

      expect(pickerRow().open).toBe(false);
      expect(picker()).toBeNull();

      openPicker();

      expect(picker()).not.toBeNull();
    });

    it('passes the picker what it needs: the exam’s scope, the questions already in it and the section’s name', () => {
      const scope: ExamScopeDto = { type: 'Book', bookId: 'b1', bookName: 'Maths', className: null, chapters: [] };
      show(section(), { examScope: scope, excluded: new Set(['q1']) });
      openPicker();

      const picked = fixture.debugElement.query((e) => e.name === 'app-question-picker').componentInstance;
      expect(picked.scope()).toEqual(scope);
      expect(Array.from(picked.excluded())).toEqual(['q1']);
      expect(picked.sectionName()).toBe('Algebra');
    });

    it('sends the question the picker hands it, with the section it is for', () => {
      show();
      openPicker();

      fixture.debugElement.query((e) => e.name === 'app-question-picker').componentInstance.picked.emit('q9');

      expect(added).toEqual([{ sectionId: 's1', questionId: 'q9' }]);
    });

    it('sends the questions the picker hands it together, with the section they are for', () => {
      show();
      openPicker();
      const sent: { sectionId: string; questionIds: readonly string[] }[] = [];
      fixture.componentInstance.addManyRequested.subscribe((r) => sent.push(r));

      fixture.debugElement.query((e) => e.name === 'app-question-picker').componentInstance.pickedMany.emit(['q7', 'q9']);

      expect(sent).toEqual([{ sectionId: 's1', questionIds: ['q7', 'q9'] }]);
      expect(added).toEqual([]);
    });

    it('has no picker on a section that cannot be changed', () => {
      show(section(), { editable: false });

      expect(pickerRow()).toBeUndefined();
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
    const SECTION_ICON = 'Remove section Algebra';

    it('has a trash icon, named for the section, that asks first, naming how many questions go with it, and removes only once confirmed', () => {
      show();
      expect(icon(SECTION_ICON).textContent?.trim()).toBe('');

      pressIcon(SECTION_ICON);
      const dialog = root.querySelector('.section-card__actions [role="alertdialog"]');
      expect(dialog?.textContent).toContain('Remove this section and its 2 questions from the exam?');
      expect(dialog?.textContent).toContain('They stay in the question bank.');
      expect(removed).toEqual([]);

      press('Remove section'); // the confirming one
      expect(removed).toEqual(['s1']);
    });

    it('goes back to normal when cancelled', () => {
      show();
      pressIcon(SECTION_ICON);

      press('Cancel');

      expect(root.querySelector('[role="alertdialog"]')).toBeNull();
      expect(icon(SECTION_ICON)).not.toBeNull();
      expect(removed).toEqual([]);
    });

    it('does not mention questions for an empty section', () => {
      show(section({ questions: [] }));

      pressIcon(SECTION_ICON);

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

    it('lists the section\'s rules and lets the author take one out, after asking', () => {
      show(section({ drawRules: [{ id: 'r1', order: 1, count: 3, bookId: null, chapterId: null, difficulty: 'easy', topic: 'algebra' }] }));
      const removedRules: { sectionId: string; ruleId: string }[] = [];
      fixture.componentInstance.drawRuleRemoveRequested.subscribe((r) => removedRules.push(r));

      expect(root.textContent).toContain('3 easy questions on algebra');
      pressIcon('Remove the rule drawing 3 easy questions on algebra');
      expect(removedRules).toEqual([]);
      expect(root.querySelector('[role="alertdialog"]')?.textContent).toContain('Remove this rule?');
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
