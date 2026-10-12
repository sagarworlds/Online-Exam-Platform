import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ExamPassingPercentage } from './exam-passing-percentage';

describe('ExamPassingPercentage', () => {
  let fixture: ComponentFixture<ExamPassingPercentage>;
  let root: HTMLElement;
  let changes: (number | null)[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ExamPassingPercentage] }).compileComponents();
  });

  function show(percent: number | null, inputs: { editable?: boolean; busy?: boolean } = {}) {
    fixture = TestBed.createComponent(ExamPassingPercentage);
    fixture.componentRef.setInput('percent', percent);
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

  it('shows an exam with no pass mark as unticked, with no percentage to fill in', () => {
    show(null);

    expect(box().checked).toBe(false);
    expect(number()).toBeNull();
    expect(root.textContent).not.toContain('%');
  });

  it('shows an exam with a pass mark as ticked, with its percentage', () => {
    show(40);

    expect(box().checked).toBe(true);
    expect(number()!.value).toBe('40');
  });

  it('offers Save only once something has changed', () => {
    show(null);
    expect(button('Save')).toBeUndefined();

    tick(true);

    expect(button('Save')).toBeDefined();
  });

  it('saves a whole percentage in range as the pass mark', () => {
    show(null);
    tick(true);

    type('55');
    submit();

    expect(changes).toEqual([55]);
  });

  it('keeps Save disabled, and saves nothing, for a value that is not a whole percentage from 1 to 100', () => {
    show(null);
    tick(true);

    type('101');
    expect(button('Save')!.disabled).toBe(true);
    submit();

    type('0');
    expect(button('Save')!.disabled).toBe(true);

    type('40.5');
    expect(button('Save')!.disabled).toBe(true);

    expect(changes).toEqual([]);
  });

  it('removes the pass mark when the author unticks it and saves', () => {
    show(40);

    tick(false);
    submit();

    expect(changes).toEqual([null]);
  });

  it('discards a change back to the mark the exam has', () => {
    show(40);
    type('60');

    button('Cancel')!.click();
    fixture.detectChanges();

    expect(number()!.value).toBe('40');
    expect(button('Save')).toBeUndefined();
  });

  it('shows the mark in words, and no form, when it cannot be changed', () => {
    show(40, { editable: false });

    expect(number()).toBeNull();
    expect(root.textContent).toContain('40% of the total marks');
  });

  it('does not save while a request about the exam is running', () => {
    show(null, { busy: true });
    tick(true);
    type('50');

    submit();

    expect(changes).toEqual([]);
  });
});
