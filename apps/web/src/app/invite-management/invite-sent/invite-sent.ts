import { Component, inject, input, signal } from '@angular/core';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { extractErrorMessage } from '../../shared/problem-details';
import { InviteApiService } from '../invite-api.service';
import { InviteHandover } from '../invite-handover/invite-handover';
import { InviteCodeDto, InviteDto } from '../invite.models';

/**
 * What staff are told once an invitation has been made (FR-14): whether the invitation email or the WhatsApp message went out, and, when
 * nothing could be sent, the link and its code to pass on by hand. When something was sent, staff can still ask for a code of their own to
 * hand over (read out, pasted into a chat). Shared by the invitation page and the invite popup on an exam's candidates page, so the two
 * say the same things.
 *
 * The page shows it for one invitation at a time and takes it away to make another, which also drops the code staff asked for.
 */
@Component({
  selector: 'app-invite-sent',
  imports: [InviteHandover, TranslatePipe],
  templateUrl: './invite-sent.html',
})
export class InviteSent {
  private readonly i18n = inject(I18nService);
  private readonly inviteApi = inject(InviteApiService);

  /** The invitation that was just made, as the API gave it back. */
  readonly invite = input.required<InviteDto>();

  /** A code staff asked for to hand over, for the invitation just made. */
  protected readonly handover = signal<InviteCodeDto | null>(null);
  protected readonly generating = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  /** The code a direct link carries, to show beside it; the link has it in its fragment. */
  protected codeOf(link: string): string {
    return new URLSearchParams(link.split('#')[1] ?? '').get('code') ?? '';
  }

  protected copyCode(invite: InviteDto): void {
    if (this.generating()) {
      return;
    }

    this.errorMessage.set(null);
    this.generating.set(true);
    this.handover.set(null);
    this.inviteApi.generateCode(invite.id).subscribe({
      next: (code) => {
        this.generating.set(false);
        this.handover.set(code);
      },
      error: (error: unknown) => {
        this.generating.set(false);
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
      },
    });
  }
}
