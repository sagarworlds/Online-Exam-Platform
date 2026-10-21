import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BrandingDto } from '../branding.models';
import { vi } from 'vitest';
import { ConfirmService } from '../../shared/confirm-dialog/confirm.service';
import { BrandingSettings } from './branding-settings';

const nothingSet: BrandingDto = { instituteName: null, primaryColour: null, hasLogo: false, updatedAtUtc: null };

describe('BrandingSettings', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<BrandingSettings>;
  let root: HTMLElement;

  const isRead = (request: { method: string; url: string }) => request.method === 'GET' && request.url.endsWith('/v1/branding');

  function typeInto(selector: string, value: string): void {
    const field = root.querySelector(selector) as HTMLInputElement;
    field.value = value;
    field.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function open(saved: BrandingDto): void {
    fixture = TestBed.createComponent(BrandingSettings);
    fixture.detectChanges();
    httpMock.expectOne(isRead).flush(saved);
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BrandingSettings],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('says that the default look is shown when nothing has been set', () => {
    open(nothingSet);

    expect(root.querySelector('.empty-state')?.textContent).toContain('default look');
    expect((root.querySelector('#branding-name') as HTMLInputElement).value).toBe('');
  });

  it('keeps Save off, and says why, while the colour is not a six-digit hex colour', () => {
    open(nothingSet);

    typeInto('#branding-colour', 'red');

    const save = root.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(save.disabled).toBe(true);
    expect(root.querySelector('.field-error')?.textContent).toContain('six-digit');
  });

  it('saves the trimmed name and colour, and then shows what the API now holds', () => {
    open(nothingSet);
    typeInto('#branding-name', '  Riverside Academy ');
    typeInto('#branding-colour', '#1a56db');

    (root.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    const save = httpMock.expectOne((request) => request.method === 'PUT' && request.url.endsWith('/v1/branding'));
    expect(save.request.body).toEqual({ instituteName: 'Riverside Academy', primaryColour: '#1a56db' });
    save.flush({ instituteName: 'Riverside Academy', primaryColour: '#1A56DB', hasLogo: false, updatedAtUtc: '2026-10-10T05:00:00Z' });
    fixture.detectChanges();

    expect(root.querySelector('.success-message')?.textContent).toContain('saved');
    expect((root.querySelector('#branding-colour') as HTMLInputElement).value).toBe('#1A56DB');
  });

  describe('removing the logo', () => {
    const withLogo: BrandingDto = { instituteName: 'Greenfield', primaryColour: null, hasLogo: true, updatedAtUtc: '2026-10-01T00:00:00Z' };
    const removeButton = () =>
      Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Remove logo') as HTMLButtonElement;
    const isRemove = (r: { method: string; url: string }) => r.method === 'DELETE' && r.url.endsWith('/v1/branding/logo');

    it('asks first, says it goes from every candidate page, and sends nothing when the answer is no', async () => {
      open(withLogo);
      const ask = vi.spyOn(TestBed.inject(ConfirmService), 'ask').mockResolvedValue(false);

      removeButton().click();
      await Promise.resolve();

      expect(ask).toHaveBeenCalledTimes(1);
      expect(ask.mock.calls[0][0]).toMatchObject({ title: 'Remove the logo?', confirmLabel: 'Remove logo', danger: true });
      expect(ask.mock.calls[0][0].message).toContain('every candidate page');
      httpMock.expectNone(isRemove);
    });

    it('removes the logo once the answer is yes, and says so', async () => {
      open(withLogo);
      vi.spyOn(TestBed.inject(ConfirmService), 'ask').mockResolvedValue(true);

      removeButton().click();
      await Promise.resolve();
      httpMock.expectOne(isRemove).flush({ ...withLogo, hasLogo: false });
      fixture.detectChanges();

      expect(root.textContent).toContain('Logo removed.');
      expect(removeButton()).toBeUndefined();
    });
  });

  it('refuses a logo that is not an accepted image type, says why, and sends nothing', () => {
    open(nothingSet);
    const input = root.querySelector('#branding-logo-file') as HTMLInputElement;
    const gif = new File(['GIF89a'], 'logo.gif', { type: 'image/gif' });
    Object.defineProperty(input, 'files', { value: { item: () => gif, length: 1 } });

    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(root.querySelector('.error-message')?.textContent).toContain('PNG, JPEG or WebP');
    httpMock.expectNone((request) => request.method === 'PUT');
  });
});
