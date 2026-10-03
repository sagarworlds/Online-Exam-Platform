/** What a keyboard shortcut on the exam page asks for. */
export type ExamShortcut = 'next' | 'previous' | 'mark' | 'clear';

const SHORTCUT_KEYS: Readonly<Record<string, ExamShortcut>> = {
  n: 'next',
  p: 'previous',
  m: 'mark',
  c: 'clear',
};

/** The keys that mean typing is going on, where a letter must stay a letter. The exam page has none today; this keeps it safe if one is added. */
const TYPING_TARGETS = new Set(['INPUT', 'TEXTAREA', 'SELECT']);

/**
 * Says which exam action, if any, a key press asks for.
 * Presses with Ctrl, Alt or Meta are ignored so browser and screen-reader shortcuts keep working, and so are
 * presses made while typing into a text field. Radio buttons are not text fields: a letter there is free to use.
 *
 * @param event The key press.
 * @returns The requested action, or null when the press is not one of ours.
 */
export function shortcutFor(event: KeyboardEvent): ExamShortcut | null {
  if (event.ctrlKey || event.altKey || event.metaKey || event.defaultPrevented) {
    return null;
  }

  const target = event.target;
  if (target instanceof HTMLElement && (target.isContentEditable || isTextEntry(target))) {
    return null;
  }

  return SHORTCUT_KEYS[event.key.toLowerCase()] ?? null;
}

function isTextEntry(element: HTMLElement): boolean {
  if (!TYPING_TARGETS.has(element.tagName)) {
    return false;
  }
  return !(element instanceof HTMLInputElement) || !['radio', 'checkbox', 'button'].includes(element.type);
}
