import { Component, Directive, booleanAttribute, computed, inject, input } from '@angular/core';

let nextId = 0;

/**
 * A form field: its label, a hint, the control, and the error, tied together the way a screen reader needs without the page writing the ids.
 * Used in place of the label, hint and error paragraphs each form rebuilt (and mostly without the ties: an error that is a bare `div`, read
 * by no one, and a field a person cannot tell is wrong).
 *
 * ```html
 * <app-form-field label="Batch name" [required]="true" hint="Shown to candidates" [error]="attempted() && !name.valid ? 'Enter a batch name' : null">
 *   <input appFieldControl type="text" formControlName="name" />
 * </app-form-field>
 * ```
 *
 * The control carries `appFieldControl`, which gives it the label's `for` target (its id), points `aria-describedby` at the hint and the error,
 * and sets `aria-invalid` while there is an error, which is also what `focusFirstInvalidFieldAfterRender` looks for. The page decides when an
 * error shows (after the first submit, as the sign-in forms do), by giving or not giving `error`.
 */
@Component({
  selector: 'app-form-field',
  template: `
    <div class="field" [class.field--invalid]="invalid()">
      <label [attr.for]="controlId">
        {{ label() }}
        @if (required()) {
          <span class="field__required" aria-hidden="true"> *</span>
        } @else if (optionalText()) {
          <span class="field__optional"> {{ optionalText() }}</span>
        }
      </label>
      @if (hint()) {
        <p class="field-hint" [id]="hintId">{{ hint() }}</p>
      }
      <ng-content />
      @if (error(); as message) {
        <p class="field-error" [id]="errorId">{{ message }}</p>
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
    }

    .field__optional {
      color: var(--text-muted);
      font-weight: 400;
    }
  `,
})
export class FormField {
  /** What the field is called. */
  readonly label = input.required<string>();

  /** What to put in it, or how it will be used. Read with the field. */
  readonly hint = input('');

  /** What is wrong with it, or null. While there is one the control is marked invalid and the message is read with it. */
  readonly error = input<string | null>(null);

  /** Whether it must be filled in: marked with a star the screen reader does not read, and `aria-required` on the control, which says it. */
  readonly required = input(false, { transform: booleanAttribute });

  /** For a field that need not be filled in: "(optional)" after the label, in the page's language. Ignored on a required field. */
  readonly optionalText = input('');

  readonly controlId = `field-${nextId++}`;
  readonly hintId = `${this.controlId}-hint`;
  readonly errorId = `${this.controlId}-error`;

  readonly invalid = computed(() => !!this.error());

  /** The ids of the hint and the error that are shown, for `aria-describedby`; null when there is neither. */
  readonly describedBy = computed(() => {
    const ids = [this.hint() ? this.hintId : null, this.error() ? this.errorId : null].filter((id): id is string => id !== null);
    return ids.length > 0 ? ids.join(' ') : null;
  });
}

/** Marks the input, select or textarea inside an `app-form-field` as its control: see {@link FormField}. */
@Directive({
  selector: '[appFieldControl]',
  host: {
    '[attr.id]': 'field.controlId',
    '[attr.aria-describedby]': 'field.describedBy()',
    '[attr.aria-invalid]': 'field.invalid() ? "true" : null',
    '[attr.aria-required]': 'field.required() ? "true" : null',
  },
})
export class FieldControl {
  protected readonly field = inject(FormField);
}
