import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { PasswordResetConfirm } from './password-reset-confirm';

describe('PasswordResetConfirm', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PasswordResetConfirm],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
  });

  function render() {
    const fixture = TestBed.createComponent(PasswordResetConfirm);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    const submitButton = compiled.querySelector('button[type="submit"]') as HTMLButtonElement;
    return { fixture, compiled, submitButton };
  }

  it('creates and renders the confirm form', () => {
    const { fixture, compiled } = render();

    expect(fixture.componentInstance).toBeTruthy();
    expect(compiled.textContent).toContain('Set a new password');
  });

  it('shows an error when the reset code has no ":" separator', () => {
    const { fixture, compiled } = render();

    fixture.componentInstance['form'].setValue({ resetCode: 'not-a-valid-code', newPassword: 'Password123!' });
    fixture.componentInstance['submit']();
    fixture.detectChanges();

    expect(compiled.textContent).toContain('Reset code must be in the form');
  });

  it('an 11-character password keeps submit disabled', () => {
    const { fixture, compiled, submitButton } = render();

    const form = fixture.componentInstance['form'];
    form.setValue({ resetCode: 'token-id:token', newPassword: 'a'.repeat(11) });
    form.controls.newPassword.markAsTouched();
    fixture.detectChanges();

    expect(submitButton.disabled).toBe(true);
    const input = compiled.querySelector('#reset-confirm-password') as HTMLInputElement;
    expect(input.getAttribute('aria-describedby')).toContain('reset-confirm-password-error');
    expect(compiled.querySelector('#reset-confirm-password-error')?.textContent).toContain('at least 12 characters');
  });

  it('shows the API weak_password detail for rules only the server checks', () => {
    const httpMock = TestBed.inject(HttpTestingController);
    const { fixture, compiled, submitButton } = render();

    fixture.componentInstance['form'].setValue({
      resetCode: 'token-id:token',
      newPassword: 'jane-correct-horse',
    });
    fixture.detectChanges();
    submitButton.click();

    httpMock
      .expectOne(`${environment.apiBaseUrl}/v1/auth/password-reset/reset`)
      .flush(
        { status: 400, title: 'weak_password', detail: 'The password must not contain your email address.' },
        { status: 400, statusText: 'Bad Request' },
      );
    fixture.detectChanges();

    expect(compiled.querySelector('.error-message')?.textContent).toContain(
      'The password must not contain your email address.',
    );
    httpMock.verify();
  });
});
