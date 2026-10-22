import { Component, ElementRef, Injector, afterNextRender, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { InviteApiService } from '../../invite-management/invite-api.service';
import { extractErrorMessage } from '../../shared/problem-details';
import { I18nService } from '../../i18n/i18n.service';

/** What changing an invited address did, for the page to say once the row it was on has gone. */
export interface InvitedAddressChange {
  from: string;
  to: string;
  invitationsMoved: number;
}

/**
 * Corrects the address on an invited student's invitations when nobody has registered with it yet: every invitation sent to the address
 * moves to the new one. A student who has an account is changed through their profile instead, which moves the invitations along with the
 * account, so the two cannot disagree.
 */
@Component({
  selector: 'app-invited-address',
  imports: [ReactiveFormsModule, TranslatePipe],
  templateUrl: './invited-address.html',
  styleUrl: './invited-address.css',
})
export class InvitedAddress {
  private readonly inviteApi = inject(InviteApiService);
  private readonly i18n = inject(I18nService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly injector = inject(Injector);

  /** The address the invitations were sent to. */
  readonly email = input.required<string>();

  /** Emitted after the invitations moved. */
  readonly changed = output<InvitedAddressChange>();

  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

  private readonly errorNotice = viewChild<ElementRef<HTMLElement>>('errorNotice');

  /** The id that ties the field to its label, unique among the open rows. */
  protected readonly fieldId = computed(() => `invited-address-${this.email().toLowerCase().replace(/[^a-z0-9]+/g, '-')}`);

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.saving()) {
      return;
    }

    const newEmail = this.form.controls.email.value.trim();
    this.saving.set(true);
    this.errorMessage.set(null);
    this.inviteApi.changeEmail(this.email(), newEmail).subscribe({
      next: (result) => {
        this.saving.set(false);
        this.changed.emit({ from: this.email(), to: newEmail, invitationsMoved: result.changed });
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(error, this.i18n.t('common.somethingWrong')));
        afterNextRender(() => this.errorNotice()?.nativeElement.focus(), { injector: this.injector });
      },
    });
  }
}
