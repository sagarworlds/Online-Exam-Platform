import { Component, ElementRef, afterNextRender, inject, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SignOutService } from '../../auth/sign-out.service';
import { TranslatePipe } from '../../i18n/translate.pipe';

/**
 * Shown to a signed-in user who opened a page their role does not allow. The heading takes focus on
 * arrival, so a screen reader announces the reason once; the user can leave through the home button or
 * sign in as someone else.
 */
@Component({
  selector: 'app-forbidden',
  imports: [RouterLink, TranslatePipe],
  templateUrl: './forbidden.html',
  styleUrl: './forbidden.css',
})
export class Forbidden {
  private readonly signOut = inject(SignOutService);

  protected readonly heading = viewChild.required<ElementRef<HTMLHeadingElement>>('heading');

  constructor() {
    // Without this, focus stays on the link the user came from, and the reason is never read out.
    afterNextRender(() => this.heading().nativeElement.focus());
  }

  /** Signs out on the server and returns to login, for a user who needs to sign in with another account. */
  protected switchAccount(): void {
    this.signOut.signOut();
  }
}
