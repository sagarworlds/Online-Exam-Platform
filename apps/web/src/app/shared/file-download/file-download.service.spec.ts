import { DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { FileDownloadService } from './file-download.service';

describe('FileDownloadService', () => {
  const originalUrl = { create: URL.createObjectURL, revoke: URL.revokeObjectURL };
  let createUrl: ReturnType<typeof vi.fn>;
  let revokeUrl: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.useFakeTimers();
    // jsdom has no object URLs of its own.
    createUrl = vi.fn(() => 'blob:report');
    revokeUrl = vi.fn();
    URL.createObjectURL = createUrl as unknown as typeof URL.createObjectURL;
    URL.revokeObjectURL = revokeUrl as unknown as typeof URL.revokeObjectURL;
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    URL.createObjectURL = originalUrl.create;
    URL.revokeObjectURL = originalUrl.revoke;
  });

  /** Spies on link clicks, noting each link as it was when clicked: its address, its file name and whether it had been added to the page. */
  function captureClicks() {
    const clicked: { href: string; download: string; inPage: boolean }[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicked.push({ href: this.getAttribute('href') ?? '', download: this.download, inPage: this.isConnected });
    });
    return clicked;
  }

  it('saves the file through a link carrying its temporary address and the given name', () => {
    const clicked = captureClicks();
    const file = new Blob(['a,b'], { type: 'text/csv' });

    TestBed.inject(FileDownloadService).save(file, 'questions.csv');

    expect(createUrl).toHaveBeenCalledWith(file);
    expect(clicked).toEqual([{ href: 'blob:report', download: 'questions.csv', inPage: false }]);
  });

  it('keeps the temporary address until after the click has been handled, then releases it', () => {
    const clicked = captureClicks();

    TestBed.inject(FileDownloadService).save(new Blob(['x']), 'a.pdf');

    expect(clicked).toHaveLength(1);
    expect(revokeUrl).not.toHaveBeenCalled();
    vi.advanceTimersByTime(999);
    expect(revokeUrl).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(revokeUrl).toHaveBeenCalledExactlyOnceWith('blob:report');
  });

  it('releases each file’s own address, so two saves in a row do not release each other’s', () => {
    captureClicks();
    createUrl.mockReturnValueOnce('blob:first').mockReturnValueOnce('blob:second');
    const service = TestBed.inject(FileDownloadService);

    service.save(new Blob(['1']), 'one.csv');
    service.save(new Blob(['2']), 'two.csv');
    vi.runAllTimers();

    expect(revokeUrl.mock.calls).toEqual([['blob:first'], ['blob:second']]);
  });

  it('makes the link in the document it was given, not the global one', () => {
    const link = document.createElement('a');
    const makeLink = vi.fn(() => link);
    const clicked = vi.spyOn(link, 'click').mockImplementation(() => undefined);
    TestBed.configureTestingModule({ providers: [{ provide: DOCUMENT, useValue: { createElement: makeLink } }] });

    TestBed.inject(FileDownloadService).save(new Blob(['x']), 'a.csv');

    expect(makeLink).toHaveBeenCalledWith('a');
    expect(link.download).toBe('a.csv');
    expect(clicked).toHaveBeenCalledOnce();
  });
});
