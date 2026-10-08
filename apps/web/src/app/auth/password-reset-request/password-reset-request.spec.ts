import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { PasswordResetRequest } from './password-reset-request';

describe('PasswordResetRequest', () => {
  const requestUrl = `${environment.apiBaseUrl}/v1/auth/password-reset/request`;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PasswordResetRequest],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
  });

  function render() {
    const fixture = TestBed.createComponent(PasswordResetRequest);
    fixture.detectChanges();
    return {
      fixture,
      compiled: fixture.nativeElement as HTMLElement,
      httpMock: TestBed.inject(HttpTestingController),
    };
  }

  it('creates and renders the request form', () => {
    const { fixture, compiled } = render();

    expect(fixture.componentInstance).toBeTruthy();
    expect(compiled.textContent).toContain('Reset password');
  });

  it('names the problem with the email once submit is pressed, and sends nothing', () => {
    const { fixture, compiled, httpMock } = render();

    (compiled.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(compiled.querySelector('#reset-request-email-error')?.textContent).toContain('Enter your email address.');
    expect(compiled.querySelector('#reset-request-email')?.getAttribute('aria-invalid')).toBe('true');
    httpMock.expectNone(requestUrl);
  });

  it('after sending, says what to do next and offers a button straight to entering the code', () => {
    const { fixture, compiled, httpMock } = render();
    fixture.componentInstance['form'].setValue({ email: 'jane@example.com' });

    (compiled.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    httpMock.expectOne(requestUrl).flush({});
    fixture.detectChanges();

    expect(compiled.querySelector('.success-message')?.textContent).toContain('a reset code has been sent');
    expect(compiled.textContent).toContain('Check your email for the code');
    const next = compiled.querySelector('a[href="/password-reset/confirm"]') as HTMLAnchorElement;
    expect(next.textContent?.trim()).toBe('Enter reset code');
  });

  it('announces a failed request as an alert above the button', () => {
    const { fixture, compiled, httpMock } = render();
    fixture.componentInstance['form'].setValue({ email: 'jane@example.com' });

    (compiled.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    httpMock
      .expectOne(requestUrl)
      .flush({ detail: 'Too many requests. Try again later.' }, { status: 429, statusText: 'Too Many Requests' });
    fixture.detectChanges();

    const alert = compiled.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Too many requests. Try again later.');
    expect(alert?.nextElementSibling?.getAttribute('type')).toBe('submit');
  });
});
