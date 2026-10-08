import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { PasswordResetConfirm } from './password-reset-confirm';

describe('PasswordResetConfirm', () => {
  const resetUrl = `${environment.apiBaseUrl}/v1/auth/password-reset/reset`;

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
    return { fixture, compiled, submitButton, httpMock: TestBed.inject(HttpTestingController) };
  }

  it('creates and renders the confirm form', () => {
    const { fixture, compiled } = render();

    expect(fixture.componentInstance).toBeTruthy();
    expect(compiled.textContent).toContain('Set a new password');
  });

  it('explains a reset code that is not complete, and sends nothing', () => {
    const { fixture, compiled, submitButton, httpMock } = render();

    fixture.componentInstance['form'].setValue({ resetCode: 'not-a-valid-code', newPassword: 'Password123!' });
    submitButton.click();
    fixture.detectChanges();

    expect(compiled.querySelector('.error-message')?.textContent).toContain('That is not a complete reset code.');
    httpMock.expectNone(resetUrl);
  });

  it('names a password that is too short on submit, and sends nothing', () => {
    const { fixture, compiled, submitButton, httpMock } = render();

    fixture.componentInstance['form'].setValue({ resetCode: 'token-id:token', newPassword: 'a'.repeat(11) });
    submitButton.click();
    fixture.detectChanges();

    expect(submitButton.disabled).toBe(false);
    const input = compiled.querySelector('#reset-confirm-password') as HTMLInputElement;
    expect(input.getAttribute('aria-describedby')).toContain('reset-confirm-password-error');
    expect(compiled.querySelector('#reset-confirm-password-error')?.textContent).toContain(
      'Password must be at least 12 characters.',
    );
    httpMock.expectNone(resetUrl);
  });

  it('shows the new password as text only while the user asks to see it', () => {
    const { fixture, compiled } = render();
    const password = compiled.querySelector('#reset-confirm-password') as HTMLInputElement;
    const toggle = compiled.querySelector('.password-toggle') as HTMLButtonElement;

    expect(password.type).toBe('password');
    toggle.click();
    fixture.detectChanges();

    expect(password.type).toBe('text');
    expect(toggle.textContent?.trim()).toBe('Hide');
  });

  it('shows the API weak_password detail for rules only the server checks', () => {
    const { fixture, compiled, submitButton, httpMock } = render();

    fixture.componentInstance['form'].setValue({
      resetCode: '  token-id:token  ',
      newPassword: 'jane-correct-horse',
    });
    fixture.detectChanges();
    submitButton.click();

    httpMock
      .expectOne(resetUrl)
      .flush(
        { status: 400, title: 'weak_password', detail: 'The password must not contain your email address.' },
        { status: 400, statusText: 'Bad Request' },
      );
    fixture.detectChanges();

    expect(compiled.querySelector('[role="alert"]')?.textContent).toContain(
      'The password must not contain your email address.',
    );
    httpMock.verify();
  });

  it('after success, confirms the update and offers a button to log in', () => {
    const { fixture, compiled, submitButton, httpMock } = render();

    fixture.componentInstance['form'].setValue({ resetCode: 'token-id:token', newPassword: 'jane-correct-horse' });
    submitButton.click();
    httpMock.expectOne(resetUrl).flush({});
    fixture.detectChanges();

    expect(compiled.querySelector('.success-message')?.textContent).toContain('Password updated.');
    const login = compiled.querySelector('a[href="/login"].btn') as HTMLAnchorElement;
    expect(login.textContent?.trim()).toBe('Log in');
  });
});
