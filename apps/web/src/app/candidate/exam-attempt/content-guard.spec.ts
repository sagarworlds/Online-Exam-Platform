import { BLOCKED_MESSAGES, BlockedAction, ContentGuard, PROTECTED_BODY_CLASS, blockedKeyAction } from './content-guard';

function key(init: KeyboardEventInit & { key: string }): KeyboardEvent {
  return new KeyboardEvent('keydown', { bubbles: true, cancelable: true, ...init });
}

describe('blockedKeyAction', () => {
  it.each([
    [{ key: 'c', ctrlKey: true }, 'copy'],
    [{ key: 'C', ctrlKey: true }, 'copy'],
    [{ key: 'c', metaKey: true }, 'copy'],
    [{ key: 'x', ctrlKey: true }, 'cut'],
    [{ key: 'v', ctrlKey: true }, 'paste'],
    [{ key: 'v', ctrlKey: true, shiftKey: true }, 'paste'],
    [{ key: 'p', ctrlKey: true }, 'print'],
    [{ key: 'p', metaKey: true }, 'print'],
    [{ key: 'p', ctrlKey: true, shiftKey: true }, 'print'],
    [{ key: 'Insert', ctrlKey: true }, 'copy'],
    [{ key: 'Insert', shiftKey: true }, 'paste'],
    [{ key: 'Delete', shiftKey: true }, 'cut'],
  ] as const)('refuses %j as %s', (init, expected) => {
    expect(blockedKeyAction(key(init))).toBe(expected);
  });

  it.each([
    [{ key: 'c' }],
    [{ key: 'v' }],
    [{ key: 'p' }],
    [{ key: 'n' }],
    [{ key: 'a', ctrlKey: true }],
    [{ key: 'f', ctrlKey: true }],
    [{ key: 'ArrowRight' }],
    [{ key: 'Delete' }],
    [{ key: 'Insert' }],
  ] as const)('leaves %j alone, so the exam shortcuts and ordinary keys still work', (init) => {
    expect(blockedKeyAction(key(init))).toBeNull();
  });

  it('leaves Ctrl+Shift+C alone: that opens the inspector, not the clipboard', () => {
    expect(blockedKeyAction(key({ key: 'C', ctrlKey: true, shiftKey: true }))).toBeNull();
  });

  it('does not take Ctrl+Alt chords, which some keyboard layouts use to type characters', () => {
    expect(blockedKeyAction(key({ key: 'c', ctrlKey: true, altKey: true }))).toBeNull();
  });
});

describe('ContentGuard', () => {
  let blocked: BlockedAction[];
  let guard: ContentGuard;

  beforeEach(() => {
    blocked = [];
    guard = new ContentGuard(document, (action) => blocked.push(action));
  });

  afterEach(() => guard.stop());

  /** Fires an event at the body and reports whether the page let it carry on. */
  function fire(type: string): boolean {
    const event = new Event(type, { bubbles: true, cancelable: true });
    document.body.dispatchEvent(event);
    return !event.defaultPrevented;
  }

  it('refuses copy, cut, paste, the context menu and dragging, and says which', () => {
    guard.start();

    expect(fire('copy')).toBe(false);
    expect(fire('cut')).toBe(false);
    expect(fire('paste')).toBe(false);
    expect(fire('contextmenu')).toBe(false);
    expect(fire('dragstart')).toBe(false);

    // Dragging is refused without a message: nobody asked to copy anything.
    expect(blocked).toEqual(['copy', 'cut', 'paste', 'contextMenu']);
  });

  it('refuses the keyboard chords and says which', () => {
    guard.start();

    for (const init of [{ key: 'c', ctrlKey: true }, { key: 'v', metaKey: true }, { key: 'p', ctrlKey: true }] as const) {
      const event = key(init);
      document.body.dispatchEvent(event);
      expect(event.defaultPrevented).toBe(true);
    }

    expect(blocked).toEqual(['copy', 'paste', 'print']);
  });

  it('does not interfere with the keys the exam uses', () => {
    guard.start();

    const next = key({ key: 'n' });
    document.body.dispatchEvent(next);

    expect(next.defaultPrevented).toBe(false);
    expect(blocked).toEqual([]);
  });

  it('refuses before anything inside the page can handle the event first', () => {
    guard.start();
    const inner = document.createElement('div');
    document.body.appendChild(inner);
    const seenByInner: boolean[] = [];
    inner.addEventListener('copy', (event) => seenByInner.push(event.defaultPrevented));

    inner.dispatchEvent(new Event('copy', { bubbles: true, cancelable: true }));

    expect(seenByInner).toEqual([true]);
    inner.remove();
  });

  it('says why a printout is empty when the browser is about to print, since the print itself cannot be cancelled', () => {
    guard.start();

    window.dispatchEvent(new Event('beforeprint'));

    expect(blocked).toEqual(['print']);
  });

  it('marks the page body while it is on, so the print stylesheet can blank the exam, and clears it again', () => {
    guard.start();
    expect(document.body.classList).toContain(PROTECTED_BODY_CLASS);

    guard.stop();
    expect(document.body.classList).not.toContain(PROTECTED_BODY_CLASS);
  });

  it('lets everything through before it is started and once it has been stopped', () => {
    expect(fire('copy')).toBe(true);

    guard.start();
    guard.stop();

    expect(fire('copy')).toBe(true);
    expect(fire('contextmenu')).toBe(true);
    expect(blocked).toEqual([]);
  });

  it('is safe to start twice (one refusal, not two) and to stop when never started', () => {
    guard.stop();
    guard.start();
    guard.start();

    fire('copy');

    expect(blocked).toEqual(['copy']);
  });

  it('still lets text be selected, which screen readers and magnifiers depend on', () => {
    guard.start();

    expect(fire('selectstart')).toBe(true);
  });

  it('has a plain message for every action it can refuse', () => {
    for (const action of ['copy', 'cut', 'paste', 'contextMenu', 'print'] as const) {
      expect(BLOCKED_MESSAGES[action]).toMatch(/turned off during this exam\.$/);
    }
  });
});
