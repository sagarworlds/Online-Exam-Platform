import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { ClipboardService } from '../../shared/clipboard/clipboard.service';

/** What the last copy did: nothing yet, put the code or the link on the clipboard, or was refused by the browser. */
type CopyState = 'none' | 'code' | 'link' | 'refused';

/**
 * An invite code (and the link that carries it) for staff to hand over by hand: read it out, paste it into a chat, send it by any
 * channel the platform does not. Both are buttons that copy and also plain selectable text, so when a browser refuses to let the page
 * use the clipboard the code can still be selected and copied by hand.
 */
@Component({
  selector: 'app-invite-handover',
  imports: [DatePipe],
  templateUrl: './invite-handover.html',
})
export class InviteHandover {
  private readonly clipboard = inject(ClipboardService);

  /** The code the invited person enters on the invitation page. */
  readonly code = input.required<string>();

  /** The same code as a link, when there is one. */
  readonly link = input<string | null>(null);

  /** The invited address, which is the only account the code works for. */
  readonly email = input.required<string>();

  /** When the code stops working, if known. */
  readonly expiresAt = input<string | null>(null);

  /** Copy each code as it arrives, for a panel that shows up because someone clicked "copy the code". */
  readonly autoCopy = input(false);

  /** Offer a Hide button, which the parent answers by removing the panel. */
  readonly dismissible = input(false);

  readonly dismissed = output<void>();

  protected readonly copied = signal<CopyState>('none');

  protected readonly message = computed(() => {
    switch (this.copied()) {
      case 'code':
        return 'Invite code copied to the clipboard.';
      case 'link':
        return 'Link copied to the clipboard.';
      case 'refused':
        return 'The browser would not let this page copy. Select the text above and copy it yourself.';
      default:
        return '';
    }
  });

  constructor() {
    // Each new code starts afresh: nothing is copied yet, and when asked to, it is copied at once. This follows the code rather than
    // the panel's creation, so another code arriving while the panel is open is copied too.
    effect(() => {
      this.code();
      untracked(() => {
        this.copied.set('none');
        if (this.autoCopy()) {
          void this.copyCode();
        }
      });
    });
  }

  protected async copyCode(): Promise<void> {
    this.copied.set((await this.clipboard.copy(this.code())) ? 'code' : 'refused');
  }

  protected async copyLink(): Promise<void> {
    const link = this.link();
    if (link) {
      this.copied.set((await this.clipboard.copy(link)) ? 'link' : 'refused');
    }
  }
}
