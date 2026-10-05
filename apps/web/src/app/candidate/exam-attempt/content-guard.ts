/** What the exam page can refuse to let a candidate do while an exam is open (FR-23). */
export type BlockedAction = 'copy' | 'cut' | 'paste' | 'contextMenu' | 'print';

/** The calm, plain sentence shown when something is refused: a statement of the rule, not a warning. */
export const BLOCKED_MESSAGES: Readonly<Record<BlockedAction, string>> = {
  copy: 'Copying is turned off during this exam.',
  cut: 'Cutting is turned off during this exam.',
  paste: 'Pasting is turned off during this exam.',
  contextMenu: 'The right-click menu is turned off during this exam.',
  print: 'Printing is turned off during this exam.',
};

/** The class put on the page body while the guard is on, which the print stylesheet uses to hide the exam from a printout. */
export const PROTECTED_BODY_CLASS = 'exam-protected';

/**
 * Says which blocked action, if any, a key press asks for. The usual chords (Ctrl or Cmd with C, X, V or P) and the older
 * Windows ones (Ctrl+Insert, Shift+Insert, Shift+Delete) are covered, with or without Shift.
 *
 * Ctrl+Shift+C is left alone: it opens the browser's inspector, not the clipboard, and this guard does not pretend to stop that.
 *
 * @param event The key press.
 * @returns The action it asks for, or null when the press is not one the guard refuses.
 */
export function blockedKeyAction(event: KeyboardEvent): BlockedAction | null {
  const key = event.key.toLowerCase();
  const command = event.ctrlKey || event.metaKey;

  if (command && !event.altKey) {
    if (key === 'c') return event.shiftKey ? null : 'copy';
    if (key === 'x') return 'cut';
    if (key === 'v') return 'paste';
    if (key === 'p') return 'print';
    if (key === 'insert') return 'copy';
  }

  if (event.shiftKey && !command) {
    if (key === 'insert') return 'paste';
    if (key === 'delete') return 'cut';
  }

  return null;
}

/**
 * Turns off copying, cutting, pasting, the right-click menu and printing on a page while it is started (FR-23).
 *
 * This is a deterrent, not a lock: a browser can always be set up to ignore a page's wishes, and nothing here can stop a
 * photograph of the screen. What it does is stop the ordinary routes (the keyboard, the context menu and the browser's own
 * menu) from carrying a question out of the exam, and tell the candidate plainly when something is refused.
 *
 * Text can still be selected. Selection is how screen readers and magnifiers work, and refusing it would shut out the
 * candidates the exam page's accessibility options are for.
 */
export class ContentGuard {
  private started = false;

  /**
   * @param document The page to guard.
   * @param onBlocked Called each time something is refused, so the page can say so.
   */
  constructor(
    private readonly document: Document,
    private readonly onBlocked: (action: BlockedAction) => void,
  ) {}

  private readonly refuse = (action: BlockedAction) => (event: Event) => {
    event.preventDefault();
    this.onBlocked(action);
  };

  private readonly onCopy = this.refuse('copy');
  private readonly onCut = this.refuse('cut');
  private readonly onPaste = this.refuse('paste');
  private readonly onContextMenu = this.refuse('contextMenu');

  private readonly onKeydown = (event: KeyboardEvent): void => {
    const action = blockedKeyAction(event);
    if (action !== null) {
      this.refuse(action)(event);
    }
  };

  // Dragging a picture or a selection out of the page is another way to carry content off it. It is refused without a
  // message: nobody asked to copy anything, so there is nothing to explain.
  private readonly onDragStart = (event: Event): void => event.preventDefault();

  // A browser's own Print menu cannot be cancelled from the page, so the print stylesheet blanks the exam out instead;
  // this only says why the printout is empty.
  private readonly onBeforePrint = (): void => this.onBlocked('print');

  /** Starts refusing. Safe to call twice. */
  start(): void {
    if (this.started) {
      return;
    }

    this.started = true;
    const doc = this.document;
    // Capture phase, so nothing inside the page can handle the event first and let it through.
    doc.addEventListener('copy', this.onCopy, true);
    doc.addEventListener('cut', this.onCut, true);
    doc.addEventListener('paste', this.onPaste, true);
    doc.addEventListener('contextmenu', this.onContextMenu, true);
    doc.addEventListener('keydown', this.onKeydown, true);
    doc.addEventListener('dragstart', this.onDragStart, true);
    doc.defaultView?.addEventListener('beforeprint', this.onBeforePrint);
    doc.body.classList.add(PROTECTED_BODY_CLASS);
  }

  /** Stops refusing and puts the page back as it was. Safe to call when not started. */
  stop(): void {
    if (!this.started) {
      return;
    }

    this.started = false;
    const doc = this.document;
    doc.removeEventListener('copy', this.onCopy, true);
    doc.removeEventListener('cut', this.onCut, true);
    doc.removeEventListener('paste', this.onPaste, true);
    doc.removeEventListener('contextmenu', this.onContextMenu, true);
    doc.removeEventListener('keydown', this.onKeydown, true);
    doc.removeEventListener('dragstart', this.onDragStart, true);
    doc.defaultView?.removeEventListener('beforeprint', this.onBeforePrint);
    doc.body.classList.remove(PROTECTED_BODY_CLASS);
  }
}
