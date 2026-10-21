import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FieldControl, FormField } from './form-field';

@Component({
  imports: [FormField, FieldControl],
  template: `
    <form>
      <app-form-field label="Batch name" [required]="true" [hint]="hint()" [error]="error()">
        <input appFieldControl type="text" />
      </app-form-field>
      <app-form-field label="Notes" optionalText="(optional)">
        <textarea appFieldControl></textarea>
      </app-form-field>
      <app-form-field label="Level" [required]="true" [error]="'Pick a level'">
        <select appFieldControl><option>One</option></select>
      </app-form-field>
    </form>
  `,
})
class Host {
  hint = signal('');
  error = signal<string | null>(null);
}

describe('FormField', () => {
  const make = () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement, host: fixture.componentInstance };
  };
  const control = (root: HTMLElement, selector: string) => root.querySelector<HTMLElement>(selector)!;

  it('ties the label to the control, so a press on the label or a screen reader finds it', () => {
    const { root } = make();

    const input = control(root, 'input');
    const label = root.querySelector('label')!;
    expect(input.id).toMatch(/^field-\d+$/);
    expect(label.getAttribute('for')).toBe(input.id);
    expect(label.textContent).toContain('Batch name');
  });

  it('gives each field its own ids, so two fields never answer to one label', () => {
    const { root } = make();

    const ids = Array.from(root.querySelectorAll('input, textarea, select')).map((c) => c.id);
    expect(new Set(ids).size).toBe(3);
  });

  it('marks a required field with a star a screen reader skips, and says it on the control instead', () => {
    const { root } = make();

    const star = root.querySelector('.field__required')!;
    expect(star.getAttribute('aria-hidden')).toBe('true');
    expect(control(root, 'input').getAttribute('aria-required')).toBe('true');
    expect(control(root, 'textarea').hasAttribute('aria-required')).toBe(false);
  });

  it('writes "(optional)" after an optional field, and not after a required one', () => {
    const { root } = make();

    const labels = Array.from(root.querySelectorAll('label')).map((l) => l.textContent?.replace(/\s+/g, ' ').trim());
    expect(labels[0]).toBe('Batch name *');
    expect(labels[1]).toBe('Notes (optional)');
  });

  it('reads the hint with the field', () => {
    const { fixture, root, host } = make();
    expect(control(root, 'input').hasAttribute('aria-describedby')).toBe(false);

    host.hint.set('Shown to candidates');
    fixture.detectChanges();

    const hint = root.querySelector('.field-hint')!;
    expect(hint.textContent).toBe('Shown to candidates');
    expect(control(root, 'input').getAttribute('aria-describedby')).toBe(hint.id);
  });

  it('marks the control invalid and reads the error with it while there is one, and takes both away when it is mended', () => {
    const { fixture, root, host } = make();
    expect(control(root, 'input').hasAttribute('aria-invalid')).toBe(false);

    host.hint.set('Shown to candidates');
    host.error.set('Enter a batch name');
    fixture.detectChanges();

    const input = control(root, 'input');
    const error = root.querySelector('.field-error')!;
    expect(error.textContent).toBe('Enter a batch name');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    // The hint and then the error, in that order.
    expect(input.getAttribute('aria-describedby')).toBe(`${root.querySelector('.field-hint')!.id} ${error.id}`);

    host.error.set(null);
    fixture.detectChanges();
    expect(root.querySelector('app-form-field:first-of-type .field-error')).toBeNull();
    expect(input.hasAttribute('aria-invalid')).toBe(false);
  });

  it('works for a select and a textarea as it does for an input, and is what the first-invalid-field focus looks for', () => {
    const { root } = make();

    expect(control(root, 'select').getAttribute('aria-invalid')).toBe('true');
    expect(root.querySelector('form [aria-invalid="true"]')).toBe(control(root, 'select'));
  });
});
