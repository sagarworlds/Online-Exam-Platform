import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthSessionService } from '../../auth/auth-session.service';
import { GuardianRegister } from './guardian-register';

/**
 * Registering as a guardian shows what the API answered.
 *
 * The app runs without zone.js, so a page is redrawn only for what a signal tells it about. These tests wait for the page to settle by
 * itself after the answer (`whenStable`) and never call `detectChanges` for it, as a manual redraw would hide a page that a browser leaves
 * on the old state: a button stuck on "Registering..." with neither a reason nor the confirmation shown.
 */
describe('GuardianRegister', () => {
  let fixture: ComponentFixture<GuardianRegister>;
  let root: HTMLElement;
  let httpMock: HttpTestingController;
  let login: ReturnType<typeof vi.fn>;

  const isRegister = (request: { method: string; url: string }) => request.method === 'POST' && request.url.endsWith('/v1/guardians');
  const submitButton = () => root.querySelector<HTMLButtonElement>('button[type="submit"]')!;

  beforeEach(async () => {
    vi.useFakeTimers({ toFake: ['setTimeout'] });
    login = vi.fn();
    await TestBed.configureTestingModule({
      imports: [GuardianRegister],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: AuthSessionService, useValue: { login } }],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    fixture = TestBed.createComponent(GuardianRegister);
    root = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  async function submit(): Promise<void> {
    fixture.componentInstance.form.patchValue({ email: 'guardian@example.com', fullName: 'Gita Rao' });
    fixture.componentInstance.onSubmit();
    await fixture.whenStable();
  }

  it('says it is registering while the request is out, and locks the button', async () => {
    await submit();

    expect(submitButton().textContent).toContain('Registering...');
    expect(submitButton().disabled).toBe(true);
    httpMock.expectOne(isRegister).flush({ id: 'guardian-1' });
  });

  it('shows why when the API refuses, and unlocks the button', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    await submit();

    httpMock.expectOne(isRegister).flush({ title: 'guardian_exists' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();

    expect(root.querySelector('.error-message')?.textContent).toContain('Failed to register guardian');
    expect(submitButton().textContent).toContain('Register');
    expect(submitButton().disabled).toBe(false);
    expect(root.querySelector('.success-message')).toBeNull();
    expect(consoleError).toHaveBeenCalled();
  });

  it('confirms the registration, signs the guardian in, and takes them to their page after a moment', async () => {
    await submit();

    httpMock.expectOne(isRegister).flush({ id: 'guardian-1' });
    await fixture.whenStable();

    expect(root.querySelector('.success-message')?.textContent).toContain('Registration successful!');
    expect(login).toHaveBeenCalledWith('guardian-1');
    expect(root.querySelector('.error-message')).toBeNull();

    const navigate = TestBed.inject(Router).navigate as ReturnType<typeof vi.fn>;
    expect(navigate).not.toHaveBeenCalled();
    vi.advanceTimersByTime(2000);
    expect(navigate).toHaveBeenCalledWith(['/guardian']);
  });
});
