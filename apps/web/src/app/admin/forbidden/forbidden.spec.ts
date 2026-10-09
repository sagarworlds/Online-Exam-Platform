import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { environment } from '../../../environments/environment';
import { AUTH_TOKEN_STORAGE_KEY, AuthSessionService } from '../../auth/auth-session.service';
import { buildFakeJwt } from '../../auth/testing/fake-jwt';
import { Forbidden } from './forbidden';

describe('Forbidden', () => {
  afterEach(() => localStorage.clear());

  function render(): HTMLElement {
    TestBed.configureTestingModule({
      imports: [Forbidden],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(Forbidden);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('tells the user they have no access, as a heading rather than an alert', () => {
    const page = render();

    expect(page.querySelector('h1')?.textContent).toContain('You don’t have access to this page');
    expect(page.querySelector('[role="alert"]')).toBeNull();
  });

  it('moves focus to the heading on arrival so the reason is read out', () => {
    const page = render();

    expect(document.activeElement).toBe(page.querySelector('h1'));
  });

  it('links home', () => {
    const page = render();

    const home = page.querySelector('a.btn') as HTMLAnchorElement;
    expect(home.getAttribute('href')).toBe('/');
    expect(home.textContent?.trim()).toBe('Go to my home page');
  });

  it('signs out on the server and returns to login when the user chooses another account', () => {
    localStorage.setItem(
      AUTH_TOKEN_STORAGE_KEY,
      buildFakeJwt({ sub: 'user-1', sid: 'session-1', exp: Math.floor(Date.now() / 1000) + 3600 }),
    );
    const page = render();
    const httpMock = TestBed.inject(HttpTestingController);
    const navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    (page.querySelector('.forbidden__switch') as HTMLButtonElement).click();
    httpMock
      .expectOne(`${environment.apiBaseUrl}/v1/auth/logout`)
      .flush(null, { status: 204, statusText: 'No Content' });

    expect(TestBed.inject(AuthSessionService).session()).toBeNull();
    expect(localStorage.getItem(AUTH_TOKEN_STORAGE_KEY)).toBeNull();
    expect(navigateByUrl).toHaveBeenCalledWith('/login');
  });
});
