import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';

/** How long the file's temporary address stays alive after the click, so the browser has read it before it is released. */
const RELEASE_DELAY_MS = 1000;

/**
 * Saves a file the app holds in memory (a blob from the API) to the user's downloads. It goes through a temporary address and a link
 * click, which is how a response that came from a POST, or one that needs the bearer token, can be saved: a plain link cannot make
 * either request.
 */
@Injectable({ providedIn: 'root' })
export class FileDownloadService {
  private readonly document = inject(DOCUMENT);

  /** Saves the file under the given name. */
  save(file: Blob, fileName: string): void {
    const url = URL.createObjectURL(file);
    const link = this.document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.click();
    // The download starts with the click, but a browser may still be reading the address after click() returns, so it is released a moment later.
    setTimeout(() => URL.revokeObjectURL(url), RELEASE_DELAY_MS);
  }
}
