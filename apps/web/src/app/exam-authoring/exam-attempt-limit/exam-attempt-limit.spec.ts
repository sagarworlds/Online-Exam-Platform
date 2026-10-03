import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ResultReleaseMode } from '../exam.models';
import { ExamAttemptLimit } from './exam-attempt-limit';

describe('ExamAttemptLimit', () => {
  let fixture: ComponentFixture<ExamAttemptLimit>;
  let root: HTMLElement;
  let changes: number[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ExamAttemptLimit] }).compileComponents();
  });

  function show(attempts = 1, inputs: { editable?: boolean; busy?: boolean; releaseMode?: ResultReleaseMode } = {}) {
    fixture = TestBed.createComponent(ExamAttemptLimit);
    fixture.componentRef.setInput('attempts', attempts);
    if (inputs.editable !== undefined) fixture.componentRef.setInput('editable', inputs.editable);
    if (inputs.busy !== undefined) fixture.componentRef.setInput('busy', inputs.busy);
    if (inputs.releaseMode !== undefined) fixture.componentRef.setInput('releaseMode', inputs.releaseMode);
    changes = [];
    fixture.componentInstance.changed.subscribe((n) => changes.push(n));
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const button = (label: string) => Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;
  const field = () => root.querySelector('#attempt-limit') as HTMLInputElement;
  const startEditing = () => {
    (root.querySelector('button[aria-label="Edit attempts allowed"]') as HTMLButtonElement).click();
    fixture.detectChanges();
  };
  const type = (value: string) => {
    field().value = value;
    field().dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const submit = () => (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
  const text = () => (root.textContent ?? '').replace(/\s+/g, ' ');

  it('says "once" for one attempt and the count for more', () => {
    show(1);
    expect(text()).toContain('Every candidate can sit this exam once.');

    fixture.componentRef.setInput('attempts', 3);
    fixture.detectChanges();
    expect(text()).toContain('Every candidate can sit this exam 3 times.');
  });

  it('reminds the author that an administrator can still give one candidate another attempt', () => {
    show(2);

    expect(text()).toContain('An administrator can still give one candidate another attempt after they have used theirs.');
  });

  describe('changing it', () => {
    it('opens a number field that starts at the current number, limited to what the API accepts', () => {
      show(4);

      startEditing();

      expect(field().value).toBe('4');
      expect(field().getAttribute('min')).toBe('1');
      expect(field().getAttribute('max')).toBe('10');
      expect(text()).toContain('From 1 to 10.');
      expect(text()).toContain('Lowering it never takes back an attempt');
    });

    it('sends the new whole number', () => {
      show(1);
      startEditing();

      type('3');
      button('Save').click();

      expect(changes).toEqual([3]);
    });

    it.each(['', '   ', '0', '-1', '11', '2.5', 'abc'])('will not send %j', (value) => {
      show(1);
      startEditing();

      type(value);

      expect(button('Save').disabled).toBe(true);
      expect(field().getAttribute('aria-invalid')).toBe('true');
      submit();
      expect(changes).toEqual([]);
    });

    it.each(['1', '10'])('accepts %s, the ends of the range', (value) => {
      show(5);
      startEditing();

      type(value);

      expect(button('Save').disabled).toBe(false);
      submit();
      expect(changes).toEqual([Number(value)]);
    });

    it('closes without sending anything when the number is unchanged', () => {
      show(2);
      startEditing();

      submit();
      fixture.detectChanges();

      expect(changes).toEqual([]);
      expect(field()).toBeNull();
    });

    it('can be backed out of with nothing sent', () => {
      show(2);
      startEditing();
      type('5');

      button('Cancel').click();
      fixture.detectChanges();

      expect(changes).toEqual([]);
      expect(field()).toBeNull();
      expect(text()).toContain('2 times');
    });

    it('will not send while a request is running', () => {
      show(1, { busy: true });
      expect(button('Edit').disabled).toBe(true);

      fixture.componentRef.setInput('busy', false);
      fixture.detectChanges();
      startEditing();
      type('2');
      fixture.componentRef.setInput('busy', true);
      fixture.detectChanges();

      expect(button('Save').disabled).toBe(true);
      expect(button('Cancel').disabled).toBe(true);
      submit();
      expect(changes).toEqual([]);
    });

    it('closes once the exam carries the new number, and stays open when the request was refused', () => {
      show(1);
      startEditing();
      type('3');

      fixture.componentRef.setInput('busy', true);
      fixture.componentRef.setInput('busy', false);
      fixture.detectChanges();
      expect(field()).not.toBeNull(); // nothing changed, so the author's typing is kept

      fixture.componentRef.setInput('attempts', 3);
      fixture.detectChanges();
      expect(field()).toBeNull();
      expect(text()).toContain('3 times');
    });
  });

  describe('when it cannot be changed', () => {
    it('has no Edit button, as for an archived exam', () => {
      show(2, { editable: false });

      expect(root.querySelector('button')).toBeNull();
      expect(text()).toContain('2 times');
    });
  });

  describe('the answer-review warning', () => {
    const warning = () => root.querySelector('.warning-note');

    it('is shown for more than one attempt when answers appear right after submitting', () => {
      show(2, { releaseMode: 'Instant' });

      expect(warning()?.textContent).toContain('candidates see the correct answers before their next attempt');
      expect(warning()?.getAttribute('role')).toBe('note');
    });

    it.each<ResultReleaseMode>(['Scheduled', 'Manual'])('is not shown when the answers are held back (%s)', (mode) => {
      show(3, { releaseMode: mode });

      expect(warning()).toBeNull();
    });

    it('is not shown for a single attempt', () => {
      show(1, { releaseMode: 'Instant' });

      expect(warning()).toBeNull();
    });

    it('follows the number being typed, so the author sees it before saving', () => {
      show(1, { releaseMode: 'Instant' });
      startEditing();
      expect(warning()).toBeNull();

      type('4');
      expect(warning()?.textContent).toContain('Answers are shown right after each attempt is submitted');

      type('1');
      expect(warning()).toBeNull();
    });
  });
});
