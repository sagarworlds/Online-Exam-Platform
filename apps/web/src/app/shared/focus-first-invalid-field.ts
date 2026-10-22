import { ElementRef, Injector, afterNextRender } from '@angular/core';

/**
 * Moves focus to the first field of the form that is marked invalid, once the form has been drawn with its errors. A candidate who pressed
 * submit with mistakes lands on the first one, and a screen reader reads that field with its error, since the error text is tied to the field
 * (WCAG 3.3.1, finding A-11 of the NFR-7 audit). Nothing is focused when no field is marked invalid.
 *
 * @param host The component's host element, which holds the form.
 * @param injector The component's injector, which the after-render hook needs.
 */
export function focusFirstInvalidFieldAfterRender(host: ElementRef<HTMLElement>, injector: Injector): void {
  afterNextRender(
    () => {
      const field = host.nativeElement.querySelector<HTMLElement>('form [aria-invalid="true"]');
      if (field === null) {
        return;
      }
      // A field inside a block that is folded away cannot take the focus: the blocks around it are opened first (a Disclosure follows).
      for (let block = field.closest('details'); block !== null; block = block.parentElement?.closest('details') ?? null) {
        block.open = true;
      }
      field.focus();
    },
    { injector },
  );
}
