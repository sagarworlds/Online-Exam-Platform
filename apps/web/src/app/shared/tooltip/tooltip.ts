import { DOCUMENT } from '@angular/common';
import { Directive, ElementRef, OnDestroy, inject, input } from '@angular/core';

/** How long the pointer rests on an icon before its tooltip appears; a keyboard focus shows it at once. */
const HOVER_DELAY_MS = 350;

/** The gap between the icon and its tooltip, and the margin kept to the edge of the screen, in pixels. */
const GAP = 6;
const EDGE = 8;

/**
 * A tooltip for an icon that has no visible words: `<button appTooltip="Edit" aria-label="Edit">`. It shows on hover after a short rest, and
 * at once on keyboard focus, so the label is never only for a mouse; Escape puts it away without moving focus, and it goes when the pointer
 * or focus leaves, or the icon scrolls out of view. It does not give the control its name: the control still carries its own `aria-label`, so a screen
 * reader reads that and the tooltip is a visual extra.
 *
 * The bubble is added to the page's body (or to the dialog the icon is in) and positioned from the icon's place on screen, so a row or card
 * that clips its content does not clip the tooltip. It sits above the icon, or below it when there is no room above, and follows the icon if the page scrolls.
 */
@Directive({
  selector: '[appTooltip]',
  host: {
    '(mouseenter)': 'showSoon()',
    '(mouseleave)': 'hide()',
    '(focus)': 'show()',
    '(blur)': 'hide()',
    '(keydown.escape)': 'hide()',
    '(click)': 'hide()',
  },
})
export class Tooltip implements OnDestroy {
  /** The words to show. Empty shows nothing. */
  readonly appTooltip = input.required<string>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly document = inject(DOCUMENT);
  private bubble: HTMLElement | null = null;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private readonly onScroll = (): void => this.follow();

  protected showSoon(): void {
    this.clearTimer();
    this.timer = setTimeout(() => this.show(), HOVER_DELAY_MS);
  }

  protected show(): void {
    this.clearTimer();
    const text = this.appTooltip().trim();
    if (text === '' || this.bubble !== null) {
      return;
    }

    const bubble = this.document.createElement('div');
    bubble.className = 'app-tooltip';
    bubble.setAttribute('role', 'tooltip');
    bubble.textContent = text;
    // A modal dialog is drawn in the browser's top layer, above everything outside it, so a bubble for an icon inside one goes inside it too.
    (this.host.nativeElement.closest('dialog') ?? this.document.body).appendChild(bubble);
    this.bubble = bubble;
    this.place(bubble);
    this.document.addEventListener('scroll', this.onScroll, true);
  }

  protected hide(): void {
    this.clearTimer();
    this.bubble?.remove();
    this.bubble = null;
    this.document.removeEventListener('scroll', this.onScroll, true);
  }

  ngOnDestroy(): void {
    this.hide();
  }

  /**
   * The page scrolled: the tooltip moves with its icon, and goes only when the icon has scrolled out of view. It must not go on any scroll,
   * because focusing an icon that is off screen makes the browser scroll it into view, which would put the tooltip away the moment it appeared.
   */
  private follow(): void {
    const bubble = this.bubble;
    if (bubble === null) {
      return;
    }

    const anchor = this.host.nativeElement.getBoundingClientRect();
    if (anchor.bottom < 0 || anchor.top > this.document.documentElement.clientHeight) {
      this.hide();
      return;
    }
    this.place(bubble);
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }

  private place(bubble: HTMLElement): void {
    const anchor = this.host.nativeElement.getBoundingClientRect();
    const box = bubble.getBoundingClientRect();
    const view = this.document.documentElement;

    const above = anchor.top - box.height - GAP;
    const top = above >= EDGE ? above : anchor.bottom + GAP;
    const centred = anchor.left + anchor.width / 2 - box.width / 2;
    const left = Math.min(Math.max(centred, EDGE), Math.max(EDGE, view.clientWidth - box.width - EDGE));

    bubble.style.top = `${Math.round(top)}px`;
    bubble.style.left = `${Math.round(left)}px`;
  }
}
