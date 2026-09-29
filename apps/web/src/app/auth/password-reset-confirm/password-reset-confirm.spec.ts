import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PasswordResetConfirm } from './password-reset-confirm';

describe('PasswordResetConfirm', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PasswordResetConfirm],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
  });

  it('creates and renders the confirm form', () => {
    const fixture = TestBed.createComponent(PasswordResetConfirm);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(fixture.componentInstance).toBeTruthy();
    expect(compiled.textContent).toContain('Set a new password');
  });

  it('shows an error when the reset code has no ":" separator', () => {
    const fixture = TestBed.createComponent(PasswordResetConfirm);
    fixture.detectChanges();

    fixture.componentInstance['form'].setValue({ resetCode: 'not-a-valid-code', newPassword: 'Password123!' });
    fixture.componentInstance['submit']();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Reset code must be in the form');
  });
});
