import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { ExamMode } from '../exam-mode/exam-mode';
import { AppUpdateNotice } from './app-update-notice';
import { AppUpdateService } from './app-update.service';

describe('AppUpdateNotice', () => {
  const updateAvailable = signal(false);
  const applying = signal(false);
  const apply = vi.fn();
  let fixture: ComponentFixture<AppUpdateNotice>;
  let examMode: ExamMode;

  const root = () => fixture.nativeElement as HTMLElement;
  const notice = () => root().querySelector('.update-notice');
  const button = () => root().querySelector<HTMLButtonElement>('.update-notice button');

  beforeEach(async () => {
    updateAvailable.set(false);
    applying.set(false);
    apply.mockReset();
    await TestBed.configureTestingModule({
      imports: [AppUpdateNotice],
      providers: [{ provide: AppUpdateService, useValue: { updateAvailable, applying, apply } }],
    }).compileComponents();
    examMode = TestBed.inject(ExamMode);
    fixture = TestBed.createComponent(AppUpdateNotice);
    fixture.detectChanges();
  });

  it('shows nothing while the app is up to date', () => {
    expect(notice()).toBeNull();
  });

  it('says so, in a polite live region, when a newer version is out, with a button to update', () => {
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
    it('stays away while a candidate is in their attempt, however long the update has been waiting', () => {
      updateAvailable.set(true);
      examMode.active.set(true);
      fixture.detectChanges();

      expect(notice()).toBeNull();
    });

    it('is not shown when an update is found in the middle of an attempt either', () => {
      examMode.active.set(true);
      fixture.detectChanges();

      updateAvailable.set(true);
      fixture.detectChanges();

      expect(notice()).toBeNull();
    });

    it('appears once the candidate leaves the attempt', () => {
      updateAvailable.set(true);
      examMode.active.set(true);
      fixture.detectChanges();
      expect(notice()).toBeNull();

      examMode.active.set(false);
      fixture.detectChanges();

      expect(notice()).not.toBeNull();
    });
  });
});
