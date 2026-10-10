import { ItemRowDto } from '../analytics.models';

/** The columns the item analysis can be sorted by. */
export type ItemSortKey = 'position' | 'attempts' | 'difficulty' | 'discrimination';

/** The order the rows are in: ascending or descending. */
export type SortDirection = 'asc' | 'desc';

/** The value a row sorts by for a key, or null when the index is withheld, so such a row has no place in the order. */
function valueOf(row: ItemRowDto, key: ItemSortKey): number | null {
  switch (key) {
    case 'position':
      return row.position;
    case 'attempts':
      return row.attempts;
    case 'difficulty':
      return row.difficulty;
    case 'discrimination':
      return row.discrimination;
  }
}

/**
 * Returns the questions sorted by a column. A withheld index (null) always sorts last, whichever way the column runs, so the figures the
 * reader can compare are at the top; ties keep the exam's own order, so the list does not shuffle between two equal rows.
 *
 * @param rows The questions, in the exam's order.
 * @param key The column to sort by.
 * @param direction Ascending or descending.
 * @returns A new array; the input is not changed.
 */
export function sortItemRows(rows: readonly ItemRowDto[], key: ItemSortKey, direction: SortDirection): ItemRowDto[] {
  const sign = direction === 'asc' ? 1 : -1;
  return [...rows].sort((a, b) => {
    const va = valueOf(a, key);
    const vb = valueOf(b, key);
    if (va === null && vb === null) {
      return a.position - b.position;
    }
    if (va === null) {
      return 1;
    }
    if (vb === null) {
      return -1;
    }
    return (va - vb) * sign || a.position - b.position;
  });
}
