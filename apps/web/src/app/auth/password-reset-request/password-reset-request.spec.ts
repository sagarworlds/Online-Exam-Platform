import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PasswordResetRequest } from './password-reset-request';

describe('PasswordResetRequest', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PasswordResetRequest],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
  });

  it('creates and renders the request form', () => {
    const fixture = TestBed.createComponent(PasswordResetRequest);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(fixture.componentInstance).toBeTruthy();
    expect(compiled.textContent).toContain('Reset password');
  });
});
