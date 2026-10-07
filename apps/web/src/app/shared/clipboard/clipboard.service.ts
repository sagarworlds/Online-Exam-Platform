import { Injectable } from '@angular/core';

/**
 * Puts text on the user's clipboard. A browser may refuse (there is no clipboard on a page that is not served securely, or the
 * user has denied the permission), so this says whether it worked instead of throwing, and a caller always keeps a way to show the
 * text for the user to select.
 */
@Injectable({ providedIn: 'root' })
export class ClipboardService {
  /** @returns true when the text is on the clipboard; false when the browser has no clipboard or refused to use it. */
  async copy(text: string): Promise<boolean> {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      // No clipboard at all is a TypeError here and a refusal is a DOMException; either way the caller falls back to selecting.
      return false;
    }
  }
}
