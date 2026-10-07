import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { QuestionApiService } from '../question-api.service';
import { QuestionFilter } from '../question.models';
import { QuestionTransfer, formatOfFile } from './question-transfer';

describe('QuestionTransfer', () => {
  let fixture: ComponentFixture<QuestionTransfer>;
  let root: HTMLElement;
  let httpMock: HttpTestingController;
  let imported: number;
  let importSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuestionTransfer],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
    importSpy = vi.spyOn(TestBed.inject(QuestionApiService), 'import');
    URL.createObjectURL = vi.fn(() => 'blob:x');
    URL.revokeObjectURL = vi.fn();
    // jsdom cannot follow a download link; the link having been clicked is all these tests need.
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
  });

  afterEach(() => httpMock.verify());

  function show(filter: QuestionFilter = {}) {
    fixture = TestBed.createComponent(QuestionTransfer);
    fixture.componentRef.setInput('filter', filter);
    imported = 0;
    fixture.componentInstance.imported.subscribe(() => imported++);
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const text = () => (root.textContent ?? '').replace(/\s+/g, ' ');
  const button = (label: string) => Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;
  const isExport = (r: { method: string; url: string }) => r.method === 'GET' && r.url.includes('/v1/questions/export');
  const isImport = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/questions/import');

  async function choose(file: File): Promise<void> {
    const input = root.querySelector('input[type="file"]') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    const calls = importSpy.mock.calls.length;
    input.dispatchEvent(new Event('change'));
    // Reading the file takes as long as the machine takes, so wait for what the read leads to rather than for a guessed time: the
    // import being sent, or the page going idle because the file was refused.
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

  describe('formatOfFile', () => {
    it('reads the format from the extension, ignoring case', () => {
      expect(formatOfFile('Bank.CSV')).toBe('csv');
      expect(formatOfFile('a.b.xlsx')).toBe('xlsx');
      expect(formatOfFile('q.json')).toBe('json');
      expect(formatOfFile('q.pdf')).toBeNull();
      expect(formatOfFile('noextension')).toBeNull();
    });
  });

  it('leaves out questions the bank already has, says which rows, and can be told to import them anyway', async () => {
    show();

    await choose(new File(['x'], 'bank.csv'));
    const first = httpMock.expectOne(isImport);
    expect(first.request.body.allowDuplicates).toBeUndefined();
    first.flush({ created: [], rejected: [], duplicates: [{ row: 2, reason: 'The bank already has this question (q1).' }] });
    fixture.detectChanges();
    expect(text()).toContain('Row 2: The bank already has this question (q1).');

    const box = root.querySelector('input[type="checkbox"]') as HTMLInputElement;
    box.checked = true;
    box.dispatchEvent(new Event('change'));
    await choose(new File(['x'], 'bank.csv'));
    const second = httpMock.expectOne(isImport);
    expect(second.request.body.allowDuplicates).toBe(true);
    second.flush({ created: [{ row: 2, id: 'q2' }], rejected: [], duplicates: [] });
  });

  it('downloads what the list shows, in the chosen format', () => {
    show({ bookId: 'b1', topic: 'fractions' });
    const select = root.querySelector('#export-format') as HTMLSelectElement;
    select.value = 'xlsx';
    select.dispatchEvent(new Event('change'));

    button('Download questions').click();

    const req = httpMock.expectOne(isExport);
    expect(req.request.params.get('format')).toBe('xlsx');
    expect(req.request.params.get('bookId')).toBe('b1');
    expect(req.request.params.get('topic')).toBe('fractions');
    req.flush(new Blob(['x']), { headers: { 'X-Questions-Skipped': '0' } });
    expect(URL.createObjectURL).toHaveBeenCalled();
    expect(text()).not.toContain('left out');
  });

  it('says how many questions an Excel export had to leave out', () => {
    show();
    button('Download questions').click();

    httpMock.expectOne(isExport).flush(new Blob(['x']), { headers: { 'X-Questions-Skipped': '2' } });
    fixture.detectChanges();

    expect(text()).toContain('2 questions were left out');
    expect(text()).toContain('CSV or JSON');
  });

  it('says why a download failed', () => {
    show();
    button('Download questions').click();

    httpMock.expectOne(isExport).flush(new Blob([JSON.stringify({ detail: 'Not allowed.' })], { type: 'application/json' }), { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')).not.toBeNull();
  });

  it('sends a CSV or JSON file as text, with its format', async () => {
    show();

    await choose(new File(['Text,Option1\nQ?,A'], 'bank.csv', { type: 'text/csv' }));

    const req = httpMock.expectOne(isImport);
    expect(req.request.body).toEqual({ format: 'csv', content: 'Text,Option1\nQ?,A' });
    req.flush({ created: [], rejected: [] });
  });

  it('sends an Excel file as base64', async () => {
    show();

    await choose(new File([new Uint8Array([80, 75, 3, 4])], 'bank.xlsx'));

    const req = httpMock.expectOne(isImport);
    expect(req.request.body).toEqual({ format: 'xlsx', content: btoa('PK\u0003\u0004') });
    req.flush({ created: [], rejected: [] });
  });

  it('reports what was created and each row left out, and tells the list to refresh', async () => {
    show();
    await choose(new File(['[]'], 'q.json'));

    httpMock.expectOne(isImport).flush({
      created: [{ row: 1, id: 'q1' }],
      rejected: [{ row: 3, errors: ['A question needs some text.'] }],
    });
    fixture.detectChanges();

    expect(text()).toContain('1 question created, 1 left out.');
    expect(text()).toContain('Row 3: A question needs some text.');
    expect(imported).toBe(1);
  });

  it('does not tell the list to refresh when nothing was created', async () => {
    show();
    await choose(new File(['[]'], 'q.json'));

    httpMock.expectOne(isImport).flush({ created: [], rejected: [{ row: 1, errors: ['No.'] }] });
    fixture.detectChanges();

    expect(imported).toBe(0);
  });

  it('refuses a file of a kind it cannot read, without asking the server', async () => {
    show();

    await choose(new File(['x'], 'notes.pdf'));
    fixture.detectChanges();

    expect(text()).toContain('Choose a .csv, .xlsx or .json file.');
    httpMock.expectNone(isImport);
  });

  it('shows the server\'s reason when the whole file is unreadable', async () => {
    show();
    await choose(new File(['{ broken'], 'q.json'));

    httpMock.expectOne(isImport).flush({ title: 'bulk_import_unreadable', detail: 'The file is not valid JSON.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(text()).toContain('The file is not valid JSON.');
    expect(imported).toBe(0);
  });
});
