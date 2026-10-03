import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MarkingSchemeDto } from '../exam.models';
import { ExamMarkingScheme } from './exam-marking-scheme';

describe('ExamMarkingScheme', () => {
  let fixture: ComponentFixture<ExamMarkingScheme>;
  let root: HTMLElement;
  let changes: MarkingSchemeDto[];

  const plain: MarkingSchemeDto = { correctMarks: 1, incorrectMarks: 0, unattemptedMarks: 0 };

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ExamMarkingScheme] }).compileComponents();
  });

  function show(scheme = plain, inputs: { editable?: boolean; busy?: boolean } = {}) {
    fixture = TestBed.createComponent(ExamMarkingScheme);
    fixture.componentRef.setInput('scheme', scheme);
    if (inputs.editable !== undefined) fixture.componentRef.setInput('editable', inputs.editable);
    if (inputs.busy !== undefined) fixture.componentRef.setInput('busy', inputs.busy);
    changes = [];
    fixture.componentInstance.changed.subscribe((s) => changes.push(s));
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const button = (label: string) => Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;
  const field = (id: 'correct' | 'incorrect' | 'unattempted') => root.querySelector(`#marks-${id}`) as HTMLInputElement;
  const startEditing = () => {
    (root.querySelector('button[aria-label="Edit marking scheme"]') as HTMLButtonElement).click();
    fixture.detectChanges();
  };
  const type = (id: 'correct' | 'incorrect' | 'unattempted', value: string) => {
    field(id).value = value;
    field(id).dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const submit = () => (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
  const text = () => (root.textContent ?? '').replace(/\s+/g, ' ');

  it('shows the three marks', () => {
    show({ correctMarks: 4, incorrectMarks: -1, unattemptedMarks: 0 });

    expect(text()).toContain('Correct answer: 4');
    expect(text()).toContain('Incorrect answer: -1');
    expect(text()).toContain('Left unanswered: 0');
  });

  it('offers no edit once the exam is published, and says why', () => {
    show(plain, { editable: false });

    expect(root.querySelector('button')).toBeNull();
    expect(text()).toContain('fixed once an exam is published');
  });

  it('opens three fields that start at the current marks', () => {
    show({ correctMarks: 4, incorrectMarks: -1, unattemptedMarks: -0.25 });

    startEditing();

    expect(field('correct').value).toBe('4');
    expect(field('incorrect').value).toBe('-1');
    expect(field('unattempted').value).toBe('-0.25');
  });

  it('sends the new marks as numbers', () => {
    show();
    startEditing();

    type('correct', '4');
    type('incorrect', '-1');
    button('Save').click();

    expect(changes).toEqual([{ correctMarks: 4, incorrectMarks: -1, unattemptedMarks: 0 }]);
  });

  it.each([
    ['correct', '0'],
    ['correct', '-1'],
    ['correct', '101'],
    ['correct', ''],
    ['correct', 'abc'],
    ['correct', '1.005'],
    ['incorrect', '1'],
    ['incorrect', '-101'],
    ['unattempted', '0.5'],
    ['unattempted', '-100.01'],
  ] as const)('will not send %s = %j', (id, value) => {
    show();
    startEditing();

    type(id, value);

    expect(button('Save').disabled).toBe(true);
    submit();
    expect(changes).toEqual([]);
  });

  it('accepts quarter marks and the ends of the range', () => {
    show();
    startEditing();

    type('correct', '100');
    type('incorrect', '-100');
    type('unattempted', '-0.25');
    submit();

    expect(changes).toEqual([{ correctMarks: 100, incorrectMarks: -100, unattemptedMarks: -0.25 }]);
  });

  it('closes without sending anything when the marks are unchanged', () => {
    show();
    startEditing();

    submit();
    fixture.detectChanges();

    expect(changes).toEqual([]);
    expect(field('correct')).toBeNull();
  });

  it('can be backed out of with nothing sent', () => {
    show();
    startEditing();
    type('correct', '5');

    button('Cancel').click();
    fixture.detectChanges();

    expect(changes).toEqual([]);
    expect(text()).toContain('Correct answer: 1');
  });

  it('closes the form once the exam carries the new marks', () => {
    show();
    startEditing();

    fixture.componentRef.setInput('scheme', { correctMarks: 4, incorrectMarks: -1, unattemptedMarks: 0 });
    fixture.detectChanges();

    expect(field('correct')).toBeNull();
    expect(text()).toContain('Correct answer: 4');
  });

  it('will not send while a request is running', () => {
    show();
    startEditing();
    type('correct', '2');
    fixture.componentRef.setInput('busy', true);
    fixture.detectChanges();

    expect(button('Save').disabled).toBe(true);
    submit();
    expect(changes).toEqual([]);
  });
});
