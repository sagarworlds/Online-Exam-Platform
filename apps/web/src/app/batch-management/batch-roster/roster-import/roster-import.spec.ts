import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { BatchApiService } from '../../batch-api.service';
import { RosterImport } from './roster-import';

describe('RosterImport', () => {
  let fixture: ComponentFixture<RosterImport>;
  let root: HTMLElement;
  let httpMock: HttpTestingController;
  let importSpy: ReturnType<typeof vi.spyOn>;
  let imported: number;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RosterImport],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
    importSpy = vi.spyOn(TestBed.inject(BatchApiService), 'importRoster');

    fixture = TestBed.createComponent(RosterImport);
    fixture.componentRef.setInput('batchId', 'batch-1');
    imported = 0;
    fixture.componentInstance.imported.subscribe(() => imported++);
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => httpMock.verify());

  const text = () => (root.textContent ?? '').replace(/\s+/g, ' ');
  const isImport = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/batches/batch-1/members/import');

  async function choose(file: File): Promise<void> {
    const input = root.querySelector('input[type="file"]') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    const calls = importSpy.mock.calls.length;
    input.dispatchEvent(new Event('change'));
    // Reading takes as long as the machine takes, so wait for what the read leads to (the import being sent, or the page going idle
    // because the file was refused) rather than for a guessed time.
    const page = fixture.componentInstance as unknown as { busy(): boolean };
    await vi.waitFor(
      () => {
        if (importSpy.mock.calls.length === calls && page.busy()) {
          throw new Error('The file is still being read.');
        }
      },
      { timeout: 5000, interval: 5 },
    );
  }

  it('sends the text of the file to the batch, and reports who was added and which rows were left out, by number', async () => {
    await choose(new File(['email\r\namy@example.com\r\nnope\r\n'], 'roster.csv'));
    const request = httpMock.expectOne(isImport);
    expect(request.request.body).toEqual({ csv: 'email\r\namy@example.com\r\nnope\r\n' });

    request.flush({
      added: [{ row: 2, memberId: 'm1' }],
      skipped: [{ row: 3, code: 'invalid_email', message: 'The e-mail address is not valid.' }],
      ignoredColumns: [],
    });
    fixture.detectChanges();

    expect(text()).toContain('1 candidate added, 1 row left out.');
    expect(text()).toContain('Row 3: The e-mail address is not valid.');
    expect(root.querySelector('[role="status"] .success-message')).not.toBeNull();
  });

  it('tells the page the member list changed only when somebody was added', async () => {
    await choose(new File(['x'], 'roster.csv'));
    httpMock.expectOne(isImport).flush({ added: [], skipped: [{ row: 1, code: 'already_in_batch', message: 'Already there.' }], ignoredColumns: [] });
    expect(imported).toBe(0);

    await choose(new File(['x'], 'roster.csv'));
    httpMock.expectOne(isImport).flush({ added: [{ row: 1, memberId: 'm1' }], skipped: [], ignoredColumns: [] });
    expect(imported).toBe(1);
  });

  it('says so, without a success tone, when nobody was added', async () => {
    await choose(new File(['x'], 'roster.csv'));
    httpMock.expectOne(isImport).flush({ added: [], skipped: [{ row: 1, code: 'batch_full', message: 'The batch has no seat left.' }], ignoredColumns: [] });
    fixture.detectChanges();

    expect(text()).toContain('0 candidates added, 1 row left out.');
    expect(root.querySelector('.success-message')).toBeNull();
  });

  it('names the columns it did not use', async () => {
    await choose(new File(['x'], 'roster.csv'));
    httpMock.expectOne(isImport).flush({ added: [{ row: 2, memberId: 'm1' }], skipped: [], ignoredColumns: ['Name', 'Roll number'] });
    fixture.detectChanges();

    expect(text()).toContain('These columns are not used: Name, Roll number.');
  });

  it('refuses a file that is not a .csv without sending it', async () => {
    await choose(new File(['x'], 'roster.xlsx'));
    fixture.detectChanges();

    expect(importSpy).not.toHaveBeenCalled();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Choose a .csv file');
  });

  it("shows the API's reason when it refuses the whole file, and does not tell the page anything changed", async () => {
    await choose(new File(['email\r\n'], 'roster.csv'));
    httpMock.expectOne(isImport).flush(
      { title: 'roster_unreadable', detail: 'The file has a header but no rows. Add one candidate\'s e-mail address per row.' },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('The file has a header but no rows.');
    expect(root.querySelector('[role="status"]')).toBeNull();
    expect(imported).toBe(0);
  });

  it('falls back to a general message when the failure has no explanation, and can be tried again', async () => {
    await choose(new File(['x'], 'roster.csv'));
    httpMock.expectOne(isImport).error(new ProgressEvent('error'), { status: 0 } as Partial<HttpErrorResponse>);
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Something went wrong');
    expect((root.querySelector('button') as HTMLButtonElement).disabled).toBe(false);
  });

  it('clears the last report when another file is chosen', async () => {
    await choose(new File(['x'], 'roster.csv'));
    httpMock.expectOne(isImport).flush({ added: [{ row: 1, memberId: 'm1' }], skipped: [], ignoredColumns: [] });
    fixture.detectChanges();
    expect(text()).toContain('1 candidate added');

    await choose(new File(['x'], 'roster.txt'));
    fixture.detectChanges();

    expect(text()).not.toContain('1 candidate added');
  });

  it('is disabled while an import is running, and the maximum row count it states is the real one', async () => {
    expect(text()).toContain('up to 1000 rows');

    await choose(new File(['x'], 'roster.csv'));
    fixture.detectChanges();
    expect((root.querySelector('button') as HTMLButtonElement).disabled).toBe(true);

    httpMock.expectOne(isImport).flush({ added: [], skipped: [], ignoredColumns: [] });
    fixture.detectChanges();
    expect((root.querySelector('button') as HTMLButtonElement).disabled).toBe(false);
  });
});
