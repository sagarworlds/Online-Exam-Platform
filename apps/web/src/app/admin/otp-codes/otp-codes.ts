import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { extractErrorMessage } from '../../shared/problem-details';
import { OtpCodesApiService } from './otp-codes-api.service';
import { OutstandingOtp } from './otp-codes.models';

/**
 * Admin page: the sign-in and registration codes candidates are waiting for, so support can read one out to a candidate
 * whose email or SMS never arrived. Nothing loads until a search is made, so a code is never shown to someone who was
 * not looking for it; every search is recorded in the audit trail by the API.
 */
@Component({
  selector: 'app-otp-codes',
  imports: [DatePipe],
  templateUrl: './otp-codes.html',
})
export class OtpCodes {
  private readonly api = inject(OtpCodesApiService);

  protected readonly search = signal('');
  protected readonly codes = signal<OutstandingOtp[] | null>(null);
  protected readonly loading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected find(): void {
    if (this.loading()) {
      return;
    }

    this.loading.set(true);
    this.errorMessage.set(null);
    this.api.list(this.search().trim() || null).subscribe({
      next: (codes) => {
        this.codes.set(codes);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.codes.set(null);
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
