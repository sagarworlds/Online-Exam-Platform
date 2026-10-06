import { Directive, ElementRef, SecurityContext, effect, inject, input } from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';

import { typesetMath } from './math-typesetter';

/**
 * Shows sanitized question HTML with its formulas rendered: `<div [appMath]="question.text"></div>` in place of
 * `[innerHTML]`. Angular still sanitizes the HTML it is given; the formulas are typeset afterwards, from text only.
 */
@Directive({ selector: '[appMath]' })
export class MathDirective {
  readonly appMath = input.required<string | null | undefined>();
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly sanitizer = inject(DomSanitizer);

  constructor() {
    effect(() => {
      this.element.innerHTML = this.sanitizer.sanitize(SecurityContext.HTML, this.appMath() ?? '') ?? '';
      typesetMath(this.element);
    });
  }
}
