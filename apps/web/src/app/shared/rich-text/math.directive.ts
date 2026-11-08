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

  /**
   * The pictures fetched so far, as object URLs, kept apart for each loader, so showing the same text again does not fetch them again.
   * A key only names a picture within one text (the API numbers each question's pictures from 0, so two questions both hold a `q-0`), and
   * a loader is what fetches for one text, so a key is looked up in its own loader's pictures and never in another's.
   */
  private readonly shown = new Map<PictureLoader, Map<string, string>>();
  private stopFetching: (() => void) | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.stopFetching?.();
      this.shown.forEach((pictures) => pictures.forEach((url) => URL.revokeObjectURL(url)));
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
          this.picturesOf(load),
        );
      }
    });
  }

  /** The pictures already fetched through one loader, which is all of one text's; empty the first time the loader is seen. */
  private picturesOf(load: PictureLoader): Map<string, string> {
    let pictures = this.shown.get(load);
    if (pictures === undefined) {
      pictures = new Map<string, string>();
      this.shown.set(load, pictures);
    }
    return pictures;
  }
}
