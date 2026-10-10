import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BrandingDto } from '../branding.models';
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
