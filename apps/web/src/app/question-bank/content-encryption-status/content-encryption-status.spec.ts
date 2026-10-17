import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { I18nService } from '../../i18n/i18n.service';
import { ContentEncryptionStatusPanel } from './content-encryption-status';

describe('ContentEncryptionStatusPanel (#57)', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<ContentEncryptionStatusPanel>;
  let root: HTMLElement;

  const isStatus = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/questions/encryption-status');

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ContentEncryptionStatusPanel],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  function open(response: { contentValues: number; plaintextValues: number; complete: boolean } | 'error') {
    fixture = TestBed.createComponent(ContentEncryptionStatusPanel);
    fixture.detectChanges();
    const request = httpMock.expectOne(isStatus);
    if (response === 'error') {
      request.flush({ title: 'server_error', detail: 'The status is unavailable.' }, { status: 500, statusText: 'Server Error' });
    } else {
      request.flush(response);
    }
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const icon = () => root.querySelector('button.encryption-status__icon') as HTMLButtonElement;
  const tip = () => root.querySelector('[role="tooltip"]') as HTMLElement;
  const showing = () => !tip().hidden;

  /** A pointer event of the given kind: a mouse hovers, a finger or a pen does not. */
  const pointer = (type: 'pointerenter' | 'pointerleave', pointerType: string) => Object.assign(new Event(type), { pointerType });

  it('is a padlock the size of a button, named for what it is, with its details in a tip it points to', () => {
    open({ contentValues: 42, plaintextValues: 0, complete: true });

    expect(icon().getAttribute('aria-label')).toBe('Question content encryption');
    expect(icon().getAttribute('aria-describedby')).toBe(tip().id);
    expect(tip().getAttribute('role')).toBe('tooltip');
    // The page shows the padlock and nothing else of this until the tip is asked for.
    expect(showing()).toBe(false);
    expect(root.querySelector('.card')).toBeNull();
  });

  it('says everything is encrypted, with the count, once the backfill has finished', () => {
    open({ contentValues: 42, plaintextValues: 0, complete: true });

    expect(icon().classList).toContain('encryption-status__icon--ok');
    expect(tip().querySelector('.badge--active')?.textContent).toContain('Encrypted');
    expect(tip().textContent).toContain('All 42 stored question values are encrypted at rest.');
    expect(tip().textContent).toContain('Question text, options, answers and version history are encrypted');
    expect(root.querySelector('[role="alert"]')).toBeNull();
  });

  it('shows the problem on the padlock itself, without opening anything, when values are still plain text', () => {
    open({ contentValues: 42, plaintextValues: 5, complete: false });

    expect(icon().classList).toContain('encryption-status__icon--bad');
    expect(icon().classList).not.toContain('encryption-status__icon--ok');
    expect(tip().querySelector('.badge--danger')?.textContent).toContain('Not complete');
    expect(tip().textContent).toContain('5 of 42 stored question values are still plain text.');
  });

  it('says there is no content yet when nothing is stored', () => {
    open({ contentValues: 0, plaintextValues: 0, complete: true });

    expect(tip().textContent).toContain('No question content is stored yet.');
  });

  describe('opening the tip', () => {
    beforeEach(() => open({ contentValues: 42, plaintextValues: 0, complete: true }));

    it('opens while a mouse is over the padlock, and closes when it leaves', () => {
      fixture.nativeElement.dispatchEvent(pointer('pointerenter', 'mouse'));
      fixture.detectChanges();
      expect(showing()).toBe(true);

      fixture.nativeElement.dispatchEvent(pointer('pointerleave', 'mouse'));
      fixture.detectChanges();
      expect(showing()).toBe(false);
    });

    it('is not opened by a finger passing over, which has no hover; a tap opens it, and a second tap closes it', () => {
      fixture.nativeElement.dispatchEvent(pointer('pointerenter', 'touch'));
      fixture.detectChanges();
      expect(showing()).toBe(false);

      icon().click();
      fixture.detectChanges();
      expect(showing()).toBe(true);

      icon().click();
      fixture.detectChanges();
      expect(showing()).toBe(false);
    });

    it('opens when the padlock is reached with the keyboard, and closes when focus moves on', () => {
      // The browser says whether focus came from the keyboard (:focus-visible); the test environment has no such notion, so it is told.
      vi.spyOn(icon(), 'matches').mockReturnValue(true);

      icon().focus();
      fixture.detectChanges();
      expect(showing()).toBe(true);

      icon().blur();
      fixture.detectChanges();
      expect(showing()).toBe(false);
    });

    it('is not opened by focus a click or a tap gave, which is not keyboard focus', () => {
      vi.spyOn(icon(), 'matches').mockReturnValue(false);

      icon().focus();
      fixture.detectChanges();

      expect(showing()).toBe(false);
    });

    it('is put away by Escape, and comes back the next time the pointer returns', () => {
      const area = fixture.nativeElement as HTMLElement;
      area.dispatchEvent(pointer('pointerenter', 'mouse'));
      fixture.detectChanges();
      expect(showing()).toBe(true);

      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
      fixture.detectChanges();
      expect(showing()).toBe(false);

      area.dispatchEvent(pointer('pointerleave', 'mouse'));
      area.dispatchEvent(pointer('pointerenter', 'mouse'));
      fixture.detectChanges();
      expect(showing()).toBe(true);
    });

    it('stays closed by a tap elsewhere on the page when it was opened by a tap', () => {
      icon().click();
      fixture.detectChanges();
      expect(showing()).toBe(true);

      document.body.click();
      fixture.detectChanges();
      expect(showing()).toBe(false);
    });
  });

  describe('when the status cannot be read', () => {
    it('shows the failure on the padlock and in its tip, and reading again is one select away', () => {
      open('error');

      expect(icon().classList).toContain('encryption-status__icon--bad');
      expect(tip().textContent).toContain('The status is unavailable.');
      expect(tip().textContent).toContain('Select the padlock to try again.');

      icon().click();
      httpMock.expectOne(isStatus).flush({ contentValues: 3, plaintextValues: 0, complete: true });
      fixture.detectChanges();

      expect(icon().classList).toContain('encryption-status__icon--ok');
      expect(tip().textContent).toContain('All 3 stored question values are encrypted at rest.');
      // Selecting to read again is not selecting to open the tip.
      expect(showing()).toBe(false);
    });
  });

  it('is in the language the administrator chose', async () => {
    open({ contentValues: 42, plaintextValues: 0, complete: true });

    await TestBed.inject(I18nService).setLanguage('hi');
    fixture.detectChanges();

    expect(icon().getAttribute('aria-label')).toBe('प्रश्न सामग्री का एन्क्रिप्शन');
    expect(tip().textContent).toContain('संग्रहीत सभी 42 प्रश्न-मान एन्क्रिप्टेड हैं।');
    await TestBed.inject(I18nService).setLanguage('en');
  });
});
