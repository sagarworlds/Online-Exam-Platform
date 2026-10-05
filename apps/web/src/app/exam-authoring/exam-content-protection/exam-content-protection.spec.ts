import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ContentProtectionRequest } from '../exam.models';
import { ExamContentProtection } from './exam-content-protection';

describe('ExamContentProtection', () => {
  let fixture: ComponentFixture<ExamContentProtection>;
  let root: HTMLElement;
  let changes: ContentProtectionRequest[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ExamContentProtection] }).compileComponents();
  });

  function show(enabled: boolean, inputs: { editable?: boolean; busy?: boolean } = {}) {
    fixture = TestBed.createComponent(ExamContentProtection);
    fixture.componentRef.setInput('enabled', enabled);
    if (inputs.editable !== undefined) fixture.componentRef.setInput('editable', inputs.editable);
    if (inputs.busy !== undefined) fixture.componentRef.setInput('busy', inputs.busy);
    changes = [];
    fixture.componentInstance.changed.subscribe((c) => changes.push(c));
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const box = () => root.querySelector('input[type="checkbox"]') as HTMLInputElement;
  const button = (label: string) =>
    Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement | undefined;
  const tick = (checked: boolean) => {
    box().checked = checked;
    box().dispatchEvent(new Event('change'));
    fixture.detectChanges();
  };
  const submit = () => (root.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

  it('shows the exam as it is: ticked when protection is on, unticked when it is off', () => {
    show(true);
    expect(box().checked).toBe(true);

    show(false);
    expect(box().checked).toBe(false);
  });

  it('offers Save only once something has changed', () => {
    show(true);
    expect(button('Save')).toBeUndefined();

    tick(false);

    expect(button('Save')).toBeDefined();
  });

  it('sends the new choice on Save', () => {
    show(true);
    tick(false);

    submit();

    expect(changes).toEqual([{ contentProtection: false }]);
  });

  it('forgets an unsaved change on Cancel', () => {
    show(true);
    tick(false);

    button('Cancel')!.click();
    fixture.detectChanges();

    expect(box().checked).toBe(true);
    expect(button('Save')).toBeUndefined();
    expect(changes).toEqual([]);
  });

  it('sends nothing when the box is ticked and unticked again', () => {
    show(true);
    tick(false);
    tick(true);

    submit();

    expect(changes).toEqual([]);
  });

  it('does not send while a request is already running, and the Save button is disabled', () => {
    show(true, { busy: true });
    tick(false);

    submit();

    expect(button('Save')!.disabled).toBe(true);
    expect(changes).toEqual([]);
  });

  it('only states the setting for an exam that can no longer be changed', () => {
    show(true, { editable: false });

    expect(box()).toBeNull();
    expect(root.textContent).toContain('turned off during the exam');
  });

  it('says it is allowed, for an exam that cannot be changed and has protection off', () => {
    show(false, { editable: false });

    expect(root.textContent).toContain('allowed');
  });

  it('is honest that this is a deterrent, and that text can still be selected', () => {
    show(true);

    expect(root.textContent).toContain('cannot stop a photograph');
    expect(root.textContent).toContain('select text');
  });
});
