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
    time: { candidatesTimed: 30, averageSeconds: 20, status: 'Shown' },
    dropOff: { reached: 30, droppedOff: 0, rate: 0 },
  });

  /** A row with its average time and its drop-off share set, the figures the two newest columns sort by. */
  const withTimeAndDropOff = (position: number, averageSeconds: number | null, rate: number | null): ItemRowDto => ({
    ...row(position, 0.5, 0),
    time: { candidatesTimed: 30, averageSeconds, status: averageSeconds === null ? 'BelowMinimumCohort' : 'Shown' },
    dropOff: { reached: 30, droppedOff: 0, rate },
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

  it('sorts by average time, with a withheld average last whichever way the column runs', () => {
    const rows = [withTimeAndDropOff(1, 40, 0), withTimeAndDropOff(2, null, 0), withTimeAndDropOff(3, 12.5, 0)];

    expect(positions(sortItemRows(rows, 'time', 'asc'))).toEqual([3, 1, 2]);
    expect(positions(sortItemRows(rows, 'time', 'desc'))).toEqual([1, 3, 2]);
  });

  it('sorts by the drop-off share, with a withheld share last whichever way the column runs', () => {
    const rows = [withTimeAndDropOff(1, 20, 0.2), withTimeAndDropOff(2, 20, null), withTimeAndDropOff(3, 20, 0.05)];

    expect(positions(sortItemRows(rows, 'dropOff', 'asc'))).toEqual([3, 1, 2]);
    expect(positions(sortItemRows(rows, 'dropOff', 'desc'))).toEqual([1, 3, 2]);
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
