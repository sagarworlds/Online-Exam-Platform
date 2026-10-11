import { ItemRowDto } from '../analytics.models';
import { sortItemRows } from './item-analysis-sort';

describe('sortItemRows', () => {
  const row = (position: number, difficulty: number | null, discrimination: number | null, attempts = 30): ItemRowDto => ({
    questionId: `q${position}`,
    position,
    text: `Question ${position}`,
    attempts,
    correctCount: 0,
    difficulty,
    discrimination,
  });

  const positions = (rows: ItemRowDto[]) => rows.map((r) => r.position);

  it('keeps the exam order when sorted by position ascending', () => {
    const rows = [row(3, 0.5, 0.1), row(1, 0.2, 0.3), row(2, 0.9, -0.2)];

    expect(positions(sortItemRows(rows, 'position', 'asc'))).toEqual([1, 2, 3]);
  });

  it('sorts by difficulty in either direction', () => {
    const rows = [row(1, 0.2, 0), row(2, 0.9, 0), row(3, 0.5, 0)];

    expect(positions(sortItemRows(rows, 'difficulty', 'asc'))).toEqual([1, 3, 2]);
    expect(positions(sortItemRows(rows, 'difficulty', 'desc'))).toEqual([2, 3, 1]);
  });

  it('sorts withheld indices last whichever way the column runs, so the figures a reader can compare come first', () => {
    const rows = [row(1, null, null), row(2, 0.4, 0.2), row(3, null, null), row(4, 0.8, -0.1)];

    expect(positions(sortItemRows(rows, 'discrimination', 'asc'))).toEqual([4, 2, 1, 3]);
    expect(positions(sortItemRows(rows, 'discrimination', 'desc'))).toEqual([2, 4, 1, 3]);
  });

  it('keeps the exam order between rows that tie on the column', () => {
    const rows = [row(4, 0.5, 0), row(2, 0.5, 0), row(3, 0.5, 0)];

    expect(positions(sortItemRows(rows, 'difficulty', 'desc'))).toEqual([2, 3, 4]);
  });

  it('does not change the rows it was given', () => {
    const rows = [row(2, 0.9, 0), row(1, 0.1, 0)];

    sortItemRows(rows, 'difficulty', 'asc');

    expect(positions(rows)).toEqual([2, 1]);
  });
});
