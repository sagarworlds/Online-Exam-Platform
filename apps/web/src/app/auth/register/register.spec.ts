import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { environment } from '../../../environments/environment';
import { toLocalIsoDate } from '../validators';
import { Register } from './register';

describe('Register', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Register],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function render() {
    const fixture = TestBed.createComponent(Register);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    const submitButton = compiled.querySelector('button[type="submit"]') as HTMLButtonElement;
    return { fixture, compiled, submitButton };
  }

  it('creates and renders the registration form', () => {
    const { fixture, compiled } = render();

    expect(fixture.componentInstance).toBeTruthy();
    expect(compiled.textContent).toContain('Register');
  });

  it('submit is disabled while the date of birth is in the future', () => {
    const { fixture, compiled, submitButton } = render();
    const inFiveDays = new Date();
    inFiveDays.setDate(inFiveDays.getDate() + 5);

    const form = fixture.componentInstance['form'];
    form.patchValue({ displayName: 'Ada', destination: 'ada@example.com', dateOfBirth: toLocalIsoDate(inFiveDays) });
    form.controls.dateOfBirth.markAsTouched();
    fixture.detectChanges();

    expect(submitButton.disabled).toBe(true);
    const dobInput = compiled.querySelector('#register-dob') as HTMLInputElement;
    expect(dobInput.getAttribute('aria-describedby')).toBe('register-dob-error');
    expect(dobInput.getAttribute('aria-invalid')).toBe('true');
    expect(compiled.querySelector('#register-dob-error')?.textContent).toContain('cannot be in the future');
  });

  it('shows an inline error for a whitespace-only display name', () => {
    const { fixture, compiled, submitButton } = render();

    const form = fixture.componentInstance['form'];
    form.patchValue({ displayName: '   ', destination: 'ada@example.com', dateOfBirth: '2000-01-01' });
    form.controls.displayName.markAsDirty();
    fixture.detectChanges();

    expect(submitButton.disabled).toBe(true);
    expect(compiled.querySelector('#register-display-name-error')?.textContent).toContain('Enter a display name.');
  });

  it('passes the destination to verify-otp as navigation state, not in the URL', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const { fixture, submitButton } = render();

    fixture.componentInstance['form'].patchValue({
      displayName: 'Ada',
      destination: 'ada@example.com',
      dateOfBirth: '2000-01-01',
    });
    fixture.detectChanges();
    submitButton.click();
    httpMock.expectOne(`${environment.apiBaseUrl}/v1/auth/register`).flush({ otpChallengeId: 'challenge-1' });

    expect(navigate).toHaveBeenCalledWith(['/verify-otp'], {
      queryParams: { challengeId: 'challenge-1', purpose: 'Registration' },
      state: { destination: 'ada@example.com' },
    });
  });

  it('shows the server error when the API rejects the date of birth', () => {
    const { fixture, compiled, submitButton } = render();

    fixture.componentInstance['form'].patchValue({
      displayName: 'Ada',
      destination: 'ada@example.com',
      dateOfBirth: '2000-01-01',
    });
    fixture.detectChanges();
    expect(submitButton.disabled).toBe(false);

    submitButton.click();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/v1/auth/register`);
    expect(req.request.body.dateOfBirth).toBe('2000-01-01');
    req.flush(
      { status: 400, title: 'invalid_date_of_birth', detail: 'Date of birth must be a real date in the past.' },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(compiled.querySelector('.error-message')?.textContent).toContain(
      'Date of birth must be a real date in the past.',
    );
  });
});
