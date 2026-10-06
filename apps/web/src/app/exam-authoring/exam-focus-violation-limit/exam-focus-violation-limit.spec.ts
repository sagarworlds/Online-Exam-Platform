import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FocusViolationLimitRequest } from '../exam.models';
import { ExamFocusViolationLimit } from './exam-focus-violation-limit';

describe('ExamFocusViolationLimit', () => {
  let fixture: ComponentFixture<ExamFocusViolationLimit>;
  let root: HTMLElement;
  let changes: FocusViolationLimitRequest[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ExamFocusViolationLimit] }).compileComponents();
  });

  function show(limit: number, inputs: { editable?: boolean; busy?: boolean } = {}) {
    fixture = TestBed.createComponent(ExamFocusViolationLimit);
    fixture.componentRef.setInput('limit', limit);
    if (inputs.editable !== undefined) fixture.componentRef.setInput('editable', inputs.editable);
    if (inputs.busy !== undefined) fixture.componentRef.setInput('busy', inputs.busy);
    changes = [];
    fixture.componentInstance.changed.subscribe((c) => changes.push(c));
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const box = () => root.querySelector('input[type="checkbox"]') as HTMLInputElement;
  const number = () => root.querySelector('input[type="number"]') as HTMLInputElement | null;
  const button = (label: string) =>
    Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement | undefined;
  const tick = (checked: boolean) => {
    box().checked = checked;
    box().dispatchEvent(new Event('change'));
    fixture.detectChanges();
  };
  const type = (text: string) => {
    number()!.value = text;
    number()!.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const submit = () => (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

  it('shows an exam that does not watch as unticked, with no number to fill in', () => {
    show(0);

    expect(box().checked).toBe(false);
    expect(number()).toBeNull();
  });

  it('shows an exam that watches as ticked, with its limit', () => {
    show(4);

    expect(box().checked).toBe(true);
    expect(number()!.value).toBe('4');
  });

  it('offers Save only once something has changed', () => {
    show(0);
    expect(button('Save')).toBeUndefined();

    tick(true);

    expect(button('Save')).toBeDefined();
  });

  it('suggests three when the author first turns it on, and sends that on Save', () => {
    show(0);
    tick(true);
    expect(number()!.value).toBe('3');

    submit();

    expect(changes).toEqual([{ focusViolationLimit: 3 }]);
  });

  it('sends the number the author typed', () => {
    show(0);
    tick(true);
    type('5');

    submit();

    expect(changes).toEqual([{ focusViolationLimit: 5 }]);
  });

  it('sends 0 when the author turns it off', () => {
    show(4);
    tick(false);

    submit();

    expect(changes).toEqual([{ focusViolationLimit: 0 }]);
  });

  it.each(['', '0', '21', '2.5', 'abc'])('will not save %j as a limit, and says the field is wrong', (text) => {
    show(2);
    type(text);

    expect(number()!.getAttribute('aria-invalid')).toBe('true');
    expect(button('Save')!.disabled).toBe(true);
    submit();
    expect(changes).toEqual([]);
  });

  it('sends nothing when the number is what the exam already has', () => {
    show(2);
    type('2');

    expect(button('Save')).toBeUndefined();
    submit();
    expect(changes).toEqual([]);
  });

  it('Cancel puts the exam back as it was', () => {
    show(0);
    tick(true);
    type('7');

    button('Cancel')!.click();
    fixture.detectChanges();

    expect(box().checked).toBe(false);
    expect(button('Save')).toBeUndefined();
  });

  it('cannot be saved while a request is running', () => {
    show(0, { busy: true });
    tick(true);

    expect(button('Save')!.disabled).toBe(true);
    submit();
    expect(changes).toEqual([]);
  });

  it('is read-only for an archived exam, and says how it stands', () => {
    show(3, { editable: false });

    expect(root.querySelector('form')).toBeNull();
    expect(root.textContent).toContain('the attempt ends after 3 times');

    show(0, { editable: false });
    expect(root.textContent).toContain('not watched');
  });
});
