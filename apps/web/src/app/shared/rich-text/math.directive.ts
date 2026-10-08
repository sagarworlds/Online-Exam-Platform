import { DestroyRef, Directive, ElementRef, SecurityContext, effect, inject, input } from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';

import { I18nService } from '../../i18n/i18n.service';
import { PictureLoader, attachLazyPictures } from './lazy-pictures';
import { typesetMath } from './math-typesetter';

/**
 * Shows sanitized question HTML with its formulas rendered: `<div [appMath]="question.text"></div>` in place of
 * `[innerHTML]`. Angular still sanitizes the HTML it is given; the formulas are typeset afterwards, from text only.
 *
 * Given a `appMathPicture` loader it also deals with a question that was sent without its pictures (FR-53, low-bandwidth mode): each
 * picture shows as a button that fetches it when pressed. Without one, the text is shown as it is.
 */
@Directive({ selector: '[appMath]' })
export class MathDirective {
  readonly appMath = input.required<string | null | undefined>();

  /** Fetches a picture the text was sent without, by its key; null when the text carries its pictures. */
  readonly appMathPicture = input<PictureLoader | null>(null);

  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly sanitizer = inject(DomSanitizer);
  private readonly i18n = inject(I18nService);

  /** The pictures fetched so far, as object URLs, so showing the same text again does not fetch them again. */
  private readonly shown = new Map<string, string>();
  private stopFetching: (() => void) | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.stopFetching?.();
      this.shown.forEach((url) => URL.revokeObjectURL(url));
      this.shown.clear();
    });

    effect(() => {
      this.stopFetching?.();
      this.stopFetching = null;
      this.element.innerHTML = this.sanitizer.sanitize(SecurityContext.HTML, this.appMath() ?? '') ?? '';
      typesetMath(this.element);

      const load = this.appMathPicture();
      if (load !== null) {
        this.stopFetching = attachLazyPictures(
          this.element,
          load,
          {
            load: (size, alt) => (alt === '' ? this.i18n.t('attempt.picture.load', { size }) : this.i18n.t('attempt.picture.loadNamed', { size, alt })),
            loading: this.i18n.t('attempt.picture.loading'),
            failed: (size) => this.i18n.t('attempt.picture.failed', { size }),
          },
          this.shown,
        );
      }
    });
  }
}
