import { Directive, ElementRef, effect, inject, input } from '@angular/core';

import { typesetMath } from './math-typesetter';

/**
 * Shows plain text with its formulas rendered: `<span [appMathText]="option.text"></span>` in place of `{{ option.text }}`. For
 * text that is never markup, such as an option or an explanation, as against `[appMath]`, which is for sanitized question HTML.
 *
 * The text goes in as text, so nothing in it can be read as markup, whatever it contains. The formulas are then typeset from
 * that text alone by the same code as the question's own, so an option is rendered exactly as the question is.
 */
@Directive({ selector: '[appMathText]' })
export class MathTextDirective {
  readonly appMathText = input.required<string | null | undefined>();

  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  constructor() {
    effect(() => {
      this.element.textContent = this.appMathText() ?? '';
      typesetMath(this.element);
    });
  }
}
