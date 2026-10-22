import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { AppUpdateNotice, isAttemptInProgress } from './app-update-notice';
import { AppUpdateService } from './app-update.service';

@Component({ template: '' })
class Page {}

describe('isAttemptInProgress', () => {
  it.each([
    ['/attempt/a1', true],
    ['/attempt/a1/', true],
    ['/attempt/a1?lang=hi', true],
    ['/attempt/a1/review', false],
    ['/attempt/a1/result', false],
    ['/attempt', false],
    ['/my-exams', false],
    ['/my-exams/e1/start', false],
    ['/admin/questions', false],
  ])('%s: %s', (url, expected) => {
    expect(isAttemptInProgress(url)).toBe(expected);
  });
});

describe('AppUpdateNotice', () => {
  const updateAvailable = signal(false);
  const applying = signal(false);
  const apply = vi.fn();
  let fixture: ComponentFixture<AppUpdateNotice>;
  let router: Router;

  const root = () => fixture.nativeElement as HTMLElement;
  const notice = () => root().querySelector('.update-notice');
  const button = () => root().querySelector<HTMLButtonElement>('.update-notice button');

  async function goTo(url: string): Promise<void> {
    await router.navigateByUrl(url);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(async () => {
    updateAvailable.set(false);
    applying.set(false);
    apply.mockReset();
    await TestBed.configureTestingModule({
      imports: [AppUpdateNotice],
      providers: [
        provideRouter([
          { path: 'attempt/:id', component: Page },
          { path: 'attempt/:id/result', component: Page },
          { path: 'my-exams', component: Page },
        ]),
        { provide: AppUpdateService, useValue: { updateAvailable, applying, apply } },
      ],
    }).compileComponents();
    router = TestBed.inject(Router);
    fixture = TestBed.createComponent(AppUpdateNotice);
    await goTo('/my-exams');
  });

  it('shows nothing while the app is up to date', () => {
    expect(notice()).toBeNull();
  });

  it('says so, in a polite live region, when a newer version is out, with a button to update', async () => {
    updateAvailable.set(true);
    fixture.detectChanges();

    expect(root().querySelector('[role="status"][aria-live="polite"]')?.contains(notice())).toBe(true);
    expect(notice()?.textContent).toContain('A new version of the app is available.');
    expect(button()?.textContent?.trim()).toBe('Update now');
  });

  it('shows again for the next version: it is the service that knows, and it says so every time', () => {
    updateAvailable.set(true);
    fixture.detectChanges();
    expect(notice()).not.toBeNull();

    updateAvailable.set(false);
    fixture.detectChanges();
    expect(notice()).toBeNull();

    updateAvailable.set(true);
    fixture.detectChanges();
    expect(notice()).not.toBeNull();
  });

  it('updates the app when the button is pressed', () => {
    updateAvailable.set(true);
    fixture.detectChanges();

    button()?.click();

    expect(apply).toHaveBeenCalledTimes(1);
  });

  it('says Updating… and cannot be pressed again while the update is under way', () => {
    updateAvailable.set(true);
    applying.set(true);
    fixture.detectChanges();

    expect(button()?.textContent?.trim()).toBe('Updating…');
    expect(button()?.disabled).toBe(true);
  });

  describe('during an exam', () => {
    it('stays away while a candidate is in their attempt, however long the update has been waiting', async () => {
      updateAvailable.set(true);
      await goTo('/attempt/a1');

      expect(notice()).toBeNull();
    });

    it('is not shown when an update is found in the middle of an attempt either', async () => {
      await goTo('/attempt/a1');

      updateAvailable.set(true);
      fixture.detectChanges();

      expect(notice()).toBeNull();
    });

    it('appears once the candidate leaves the attempt', async () => {
      updateAvailable.set(true);
      await goTo('/attempt/a1');
      expect(notice()).toBeNull();

      await goTo('/attempt/a1/result');

      expect(notice()).not.toBeNull();
    });
  });
});
