import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { BatchApiService } from '../batch-api.service';
import { BatchRoster } from './batch-roster';

describe('BatchRoster', () => {
  let fixture: ComponentFixture<BatchRoster>;
  let root: HTMLElement;
  let httpMock: HttpTestingController;
  let importSpy: ReturnType<typeof vi.spyOn>;

  const isMembersRead = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/batches/batch-1/members');
  const isImport = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/batches/batch-1/members/import');

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BatchRoster],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { params: { id: 'batch-1' } } } },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
    importSpy = vi.spyOn(TestBed.inject(BatchApiService), 'importRoster');

    fixture = TestBed.createComponent(BatchRoster);
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
    httpMock.expectOne(isMembersRead).flush([]);
    fixture.detectChanges();
  });

  afterEach(() => httpMock.verify());

  async function importFile(): Promise<void> {
    const input = root.querySelector('input[type="file"]') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [new File(['amy@example.com'], 'roster.csv')], configurable: true });
    const calls = importSpy.mock.calls.length;
    input.dispatchEvent(new Event('change'));
    await vi.waitFor(
      () => {
        if (importSpy.mock.calls.length === calls) {
          throw new Error('The file is still being read.');
        }
      },
      { timeout: 5000, interval: 5 },
    );
  }

  it('offers the roster import for this batch, below the single add form', () => {
    expect(root.querySelector('app-roster-import')).not.toBeNull();
    expect(root.querySelector('form')).not.toBeNull();
  });

  it('reads the members again after an import that added somebody, so the new seats show', async () => {
    await importFile();
    httpMock.expectOne(isImport).flush({ added: [{ row: 1, memberId: 'm1' }], skipped: [], ignoredColumns: [] });

    httpMock.expectOne(isMembersRead).flush([{ id: 'm1', email: 'amy@example.com', status: 'Pending' }]);
    fixture.detectChanges();

    expect(root.textContent).toContain('amy@example.com');
    expect(root.textContent).toContain('Members (1)');
  });

  describe('the refresh icon', () => {
    const icon = () => root.querySelector<HTMLButtonElement>('app-refresh-button button')!;

    it('sits in the page header, beside the title', () => {
      expect(root.querySelector('.page-header h1')?.textContent).toContain('Batch Roster');
      expect(root.querySelector('.page-header app-refresh-button')).not.toBeNull();
    });

    it('reads the members again without blanking the list, and shows one who registered since', () => {
      icon().click();
      fixture.detectChanges();
      expect(icon().getAttribute('aria-busy')).toBe('true');
      expect(root.textContent).not.toContain('Loading members');

      httpMock.expectOne(isMembersRead).flush([{ id: 'm1', email: 'amy@example.com', status: 'Registered' }]);
      fixture.detectChanges();

      expect(root.textContent).toContain('amy@example.com');
      expect(root.textContent).toContain('Members (1)');
      expect(icon().hasAttribute('aria-busy')).toBe(false);
    });

    it('keeps the members and says why when the read fails, instead of showing an empty roster', () => {
      icon().click();
      httpMock.expectOne(isMembersRead).flush([{ id: 'm1', email: 'amy@example.com', status: 'Registered' }]);
      fixture.detectChanges();

      icon().click();
      httpMock.expectOne(isMembersRead).flush({ title: 'server_error', detail: 'The members are not available.' }, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      expect(root.textContent).toContain('amy@example.com');
      expect(root.querySelector('.error-message')?.textContent).toContain('The members are not available.');
      expect(root.textContent).not.toContain('No members added yet');
    });

    it('says the members could not be read, not that there are none, and clears that once it works', () => {
      icon().click();
      httpMock.expectOne(isMembersRead).flush({}, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      expect(root.querySelector('.error-message')?.textContent).toContain('The members could not be loaded');
      expect(root.textContent).not.toContain('No members added yet');

      icon().click();
      httpMock.expectOne(isMembersRead).flush([]);
      fixture.detectChanges();
      expect(root.querySelector('.error-message')).toBeNull();
      expect(root.textContent).toContain('No members added yet');
    });
  });

  it('does not read the members again when the import added nobody', async () => {
    await importFile();
    httpMock.expectOne(isImport).flush({ added: [], skipped: [{ row: 1, code: 'already_in_batch', message: 'Already there.' }], ignoredColumns: [] });

    httpMock.expectNone(isMembersRead);
  });
});
