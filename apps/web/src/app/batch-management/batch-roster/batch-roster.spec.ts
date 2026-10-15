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

  it('does not read the members again when the import added nobody', async () => {
    await importFile();
    httpMock.expectOne(isImport).flush({ added: [], skipped: [{ row: 1, code: 'already_in_batch', message: 'Already there.' }], ignoredColumns: [] });

    httpMock.expectNone(isMembersRead);
  });
});
