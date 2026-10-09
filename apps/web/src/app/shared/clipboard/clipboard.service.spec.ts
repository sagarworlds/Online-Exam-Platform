import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { ClipboardService } from './clipboard.service';

describe('ClipboardService', () => {
  // The test browser has no clipboard of its own, so each test provides (or withholds) one.
  function stubClipboard(value: unknown): void {
    Object.defineProperty(navigator, 'clipboard', { value, configurable: true });
  }

  afterEach(() => stubClipboard(undefined));

  const service = () => TestBed.inject(ClipboardService);

  it('puts the text on the clipboard and says so', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    stubClipboard({ writeText });

    expect(await service().copy('K7M2QX9A')).toBe(true);
    expect(writeText).toHaveBeenCalledWith('K7M2QX9A');
  });

  it('says it did not work when the browser refuses', async () => {
    stubClipboard({ writeText: vi.fn().mockRejectedValue(new DOMException('denied', 'NotAllowedError')) });

    expect(await service().copy('K7M2QX9A')).toBe(false);
  });

  it('says it did not work when there is no clipboard at all, as on a page that is not served securely', async () => {
    stubClipboard(undefined);

    expect(await service().copy('K7M2QX9A')).toBe(false);
  });
});
