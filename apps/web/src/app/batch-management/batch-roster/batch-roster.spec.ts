import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { ConfirmService } from '../../shared/confirm-dialog/confirm.service';
import { ToastService } from '../../shared/toast/toast.service';
import { BatchApiService } from '../batch-api.service';
import { BatchRoster } from './batch-roster';

describe('BatchRoster', () => {
  let fixture: ComponentFixture<BatchRoster>;
  let root: HTMLElement;
  let httpMock: HttpTestingController;
  let importSpy: ReturnType<typeof vi.spyOn>;

  const isMembersRead = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/batches/batch-1/members');
  const isBatchesRead = (r: { method: string; url: string }) => r.method === 'GET' && r.url.endsWith('/v1/batches');
  const isAdd = (r: { method: string; url: string }) => r.method === 'POST' && r.url.endsWith('/v1/batches/batch-1/members');
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
    // There is no read of one batch, so the heading's name comes from the list of batches.
    httpMock.expectOne(isBatchesRead).flush([{ id: 'batch-0', name: 'Other batch' }, { id: 'batch-1', name: 'Morning batch' }]);
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
      expect(root.querySelector('.page-header h1')?.textContent).toContain('Roster: Morning batch');
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

  describe('the heading', () => {
    it('names the batch, so a person with two rosters open knows which this is', () => {
      expect(root.querySelector('h1')?.textContent?.trim()).toBe('Roster: Morning batch');
    });

    it('is the plain one when the name could not be read, and the roster still works', () => {
      const other = TestBed.createComponent(BatchRoster);
      other.detectChanges();
      httpMock.expectOne(isMembersRead).flush([]);
      httpMock.expectOne(isBatchesRead).flush({}, { status: 500, statusText: 'Server Error' });
      other.detectChanges();

      expect((other.nativeElement as HTMLElement).querySelector('h1')?.textContent?.trim()).toBe('Batch roster');
    });
  });

  describe('searching the members', () => {
    const members = [
      { id: 'm1', email: 'amy@example.com', phone: '98200 11111', status: 'Registered' },
      { id: 'm2', email: 'ben@example.com', status: 'Pending' },
      { id: 'm3', email: 'cara@school.org', phone: '98200 22222', status: 'Verified' },
    ];
    const search = (value: string) => {
      const input = root.querySelector('input[type="search"]') as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };
    const emails = () => Array.from(root.querySelectorAll('tbody th')).map((c) => c.textContent?.trim());

    beforeEach(() => {
      (root.querySelector('app-refresh-button button') as HTMLButtonElement).click();
      httpMock.expectOne(isMembersRead).flush(members);
      fixture.detectChanges();
    });

    it('is offered once there are members, and not before', () => {
      expect(root.querySelector('input[type="search"]')).not.toBeNull();
      expect(root.querySelector('label[for]')?.textContent).toBeDefined();
    });

    it('narrows the list by email, by phone number, and ignores case', () => {
      search('SCHOOL');
      expect(emails()).toEqual(['cara@school.org']);

      search('22222');
      expect(emails()).toEqual(['cara@school.org']);

      search('example');
      expect(emails()).toEqual(['amy@example.com', 'ben@example.com']);
    });

    it('says how many of the members are shown', () => {
      expect(root.querySelector('h2#members-heading')?.textContent).toContain('Members (3)');

      search('example');
      expect(root.querySelector('h2#members-heading')?.textContent).toContain('Members (2 of 3)');
    });

    it('says nothing matches, rather than showing an empty table, and clears the search from there', () => {
      search('zzz');
      expect(root.querySelector('table')).toBeNull();
      expect(root.textContent).toContain('No member matches the search.');

      (Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.includes('Clear search')) as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(emails().length).toBe(3);
    });
  });

  describe('removing a member', () => {
    const members = [
      { id: 'm1', email: 'amy@example.com', phone: '98200 11111', status: 'Registered' },
      { id: 'm2', email: 'ben@example.com', status: 'Pending' },
      { id: 'm3', email: 'cara@school.org', status: 'Verified' },
    ];
    const isRemove = (id: string) => (r: { method: string; url: string }) => r.method === 'DELETE' && r.url.endsWith(`/v1/batches/batch-1/members/${id}`);
    const removeButton = (email: string) => root.querySelector<HTMLButtonElement>(`button[aria-label="Remove ${email} from the batch"]`)!;
    const answer = (confirmed: boolean) => vi.spyOn(TestBed.inject(ConfirmService), 'ask').mockResolvedValue(confirmed);

    beforeEach(() => {
      (root.querySelector('app-refresh-button button') as HTMLButtonElement).click();
      httpMock.expectOne(isMembersRead).flush(members);
      fixture.detectChanges();
    });

    it('has a Remove button for each member, in words, named for the member', () => {
      expect(Array.from(root.querySelectorAll('tbody tr')).map((row) => row.querySelector('button')?.textContent?.trim())).toEqual(['Remove', 'Remove', 'Remove']);
      expect(removeButton('ben@example.com')).not.toBeNull();
      expect(root.querySelector('thead')?.textContent).toContain('Actions');
    });

    it('asks first, saying what removing does, and sends nothing when the answer is no', async () => {
      const ask = answer(false);

      removeButton('ben@example.com').click();
      await fixture.whenStable();

      expect(ask).toHaveBeenCalledOnce();
      const options = ask.mock.calls[0][0];
      expect(options.title).toBe('Remove ben@example.com from this batch?');
      expect(options.danger).toBe(true);
      expect(options.confirmLabel).toBe('Remove');
      expect(options.consequences).toEqual(["They no longer appear on this batch's rankings.", 'You can add the address again later.']);
      httpMock.expectNone((r) => r.method === 'DELETE');
    });

    it('removes the member once confirmed, says so, and reads the roster again for who is left', async () => {
      answer(true);

      removeButton('ben@example.com').click();
      await fixture.whenStable();
      httpMock.expectOne(isRemove('m2')).flush(null, { status: 204, statusText: 'No Content' });
      httpMock.expectOne(isMembersRead).flush([members[0], members[2]]);
      fixture.detectChanges();

      expect(TestBed.inject(ToastService).toasts().map((t) => [t.kind, t.message])).toEqual([['success', 'ben@example.com is removed from the batch.']]);
      expect(Array.from(root.querySelectorAll('tbody th')).map((c) => c.textContent?.trim())).toEqual(['amy@example.com', 'cara@school.org']);
      expect(root.querySelector('h2#members-heading')?.textContent).toContain('Members (2)');
    });

    it('does not start a second removal while the first is on its way', async () => {
      answer(true);

      removeButton('ben@example.com').click();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(removeButton('amy@example.com').getAttribute('aria-disabled')).toBe('true');
      removeButton('amy@example.com').click();
      await fixture.whenStable();

      httpMock.expectOne((r) => r.method === 'DELETE').flush(null, { status: 204, statusText: 'No Content' });
      httpMock.expectOne(isMembersRead).flush(members);
    });

    it('says why when the member could not be removed, keeps the list as it was, and lets another try', async () => {
      answer(true);

      removeButton('ben@example.com').click();
      await fixture.whenStable();
      httpMock.expectOne(isRemove('m2')).flush({ title: 'batch_member_not_found', detail: 'Batch member not found.' }, { status: 404, statusText: 'Not Found' });
      fixture.detectChanges();

      expect(TestBed.inject(ToastService).toasts().map((t) => [t.kind, t.message])).toEqual([['error', 'Batch member not found.']]);
      expect(root.querySelectorAll('tbody tr')).toHaveLength(3);
      expect(removeButton('ben@example.com').hasAttribute('aria-disabled')).toBe(false);
    });

    it('puts focus on the row that takes the removed one’s place, not on the top of the page', async () => {
      answer(true);
      document.body.appendChild(root);
      removeButton('ben@example.com').focus();

      removeButton('ben@example.com').click();
      await fixture.whenStable();
      httpMock.expectOne(isRemove('m2')).flush(null, { status: 204, statusText: 'No Content' });
      httpMock.expectOne(isMembersRead).flush([members[0], members[2]]);
      fixture.detectChanges();
      await fixture.whenStable();

      expect(document.activeElement).toBe(removeButton('cara@school.org'));
      root.remove();
    });

    it('puts focus on the add form when the last member was removed', async () => {
      answer(true);
      document.body.appendChild(root);
      removeButton('amy@example.com').click();
      await fixture.whenStable();
      httpMock.expectOne(isRemove('m1')).flush(null, { status: 204, statusText: 'No Content' });
      httpMock.expectOne(isMembersRead).flush([]);
      fixture.detectChanges();
      await fixture.whenStable();

      expect(document.activeElement).toBe(root.querySelector('input[type="email"]'));
      root.remove();
    });
  });

  describe('adding a member', () => {
    const fillEmail = (value: string) => {
      const input = root.querySelector('input[type="email"]') as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    const submit = () => {
      root.querySelector('form')!.dispatchEvent(new Event('submit'));
      fixture.detectChanges();
    };

    it('does not switch Add member off: pressing it with no address says so, and sends nothing', () => {
      expect((root.querySelector('form button[type="submit"]') as HTMLButtonElement).disabled).toBe(false);

      submit();

      expect(root.querySelector('.field-error')?.textContent).toContain('Enter a valid email address.');
      httpMock.expectNone(isAdd);
    });

    it('adds the member, says so, clears the form and reads the members again', () => {
      fillEmail('amy@example.com');
      submit();

      httpMock.expectOne(isAdd).flush(null);
      httpMock.expectOne(isMembersRead).flush([{ id: 'm1', email: 'amy@example.com', status: 'Pending' }]);
      fixture.detectChanges();

      expect(TestBed.inject(ToastService).toasts().map((t) => t.message)).toEqual(['The member is added.']);
      expect((root.querySelector('input[type="email"]') as HTMLInputElement).value).toBe('');
      expect(root.querySelector('.field-error')).toBeNull();
      expect(root.textContent).toContain('Members (1)');
    });

    it('shows the reason the server gives when the member cannot be added', () => {
      fillEmail('amy@example.com');
      submit();

      httpMock.expectOne(isAdd).flush({ detail: 'The batch is full.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(root.querySelector('.card .error-message')?.textContent).toContain('The batch is full.');
    });
  });

  it('does not read the members again when the import added nobody', async () => {
    await importFile();
    httpMock.expectOne(isImport).flush({ added: [], skipped: [{ row: 1, code: 'already_in_batch', message: 'Already there.' }], ignoredColumns: [] });

    httpMock.expectNone(isMembersRead);
  });
});
