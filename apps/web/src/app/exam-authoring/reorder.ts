/** Which way to move something in a list. */
export type MoveDirection = 'up' | 'down';

/**
 * The list with one id moved a place toward the start (`up`) or the end (`down`), as the full order the API asks for. Null when the id is not
 * in the list, or is already at that end, so a caller sends nothing for a move that goes nowhere.
 */
export function movedBy(ids: readonly string[], id: string, direction: MoveDirection): string[] | null {
  const from = ids.indexOf(id);
  const to = direction === 'up' ? from - 1 : from + 1;
  if (from < 0 || to < 0 || to >= ids.length) {
    return null;
  }

  const next = [...ids];
  [next[from], next[to]] = [next[to], next[from]];
  return next;
}
