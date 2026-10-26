import { Location } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { vi } from 'vitest';
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

  describe('opened from the link in the e-mail', () => {
    const tokenId = '3f2a9c1e-0b7d-4c55-9a60-1d2e3f4a5b6c';
    const secret = 'ABCDEF0123456789';

    /** Renders the page as the router would for `/password-reset/confirm#<fragment>`, with the address bar's history replaced by a spy. */
    function renderFromLink(fragment: string | null) {
      const replaceState = vi.fn();
      TestBed.configureTestingModule({
        providers: [
          { provide: ActivatedRoute, useValue: { snapshot: { fragment } } },
          { provide: Location, useValue: { path: () => '/password-reset/confirm', replaceState } },
        ],
      });
      return { ...render(), replaceState };
    }

    it('fills in the code, hides the code box, and takes the code off the address', () => {
      const { compiled, replaceState } = renderFromLink(`code=${tokenId}%3A${secret}`);

      expect(compiled.querySelector('#reset-confirm-code')).toBeNull();
      expect(compiled.querySelector('[role="status"]')?.textContent).toContain('filled in from the link in your email');
      // The code is a credential: it must not stay in the address bar or the history entry once the page has it.
      expect(replaceState).toHaveBeenCalledExactlyOnceWith('/password-reset/confirm');
    });

    it('sends the code of the link, split into its id and secret, with the chosen password', () => {
      const { fixture, submitButton, httpMock } = renderFromLink(`code=${tokenId}%3A${secret}`);
      fixture.componentInstance['form'].controls.newPassword.setValue('jane-correct-horse');

      submitButton.click();

      const request = httpMock.expectOne(resetUrl);
      expect(request.request.body).toEqual({ passwordResetTokenId: tokenId, token: secret, newPassword: 'jane-correct-horse' });
      request.flush({});
    });

    it('brings the code box back, empty and focused, when the person asks to type the code instead', async () => {
      const { fixture, compiled } = renderFromLink(`code=${tokenId}%3A${secret}`);

      (compiled.querySelector('button.link') as HTMLButtonElement).click();
      fixture.detectChanges();
      await fixture.whenStable();

      const box = compiled.querySelector('#reset-confirm-code') as HTMLInputElement;
      expect(box.value).toBe('');
      expect(compiled.querySelector('[role="status"]')).toBeNull();
      expect(document.activeElement).toBe(box);
    });

    it.each([['code=not-a-code'], ['code='], ['other=1'], [null]])(
      'leaves the code box and the address alone when the fragment (%s) holds no complete code',
      (fragment) => {
        const { compiled, replaceState } = renderFromLink(fragment);

        const box = compiled.querySelector('#reset-confirm-code') as HTMLInputElement;
        expect(box.value).toBe('');
        expect(replaceState).not.toHaveBeenCalled();
      },
    );
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
