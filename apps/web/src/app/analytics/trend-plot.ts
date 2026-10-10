/** Where one result sits on the trend: its position in the order sat, and where its point is drawn. */
export interface TrendPoint {
  /** The result's place in the trend, from 0. Results with no marks keep their place even though they are not drawn. */
  index: number;
  x: number;
  y: number;
  /** The share of the marks earned, in percent. */
  percent: number;
}

/** A horizontal gridline and the percentage it marks. */
export interface TrendGridLine {
  percent: number;
  y: number;
}

/** The drawn trend: the points, the polyline through them, and the gridlines. */
export interface TrendPlot {
  points: TrendPoint[];
  /** The `points` attribute of the line joining the drawn points, empty when there are fewer than two. */
  polyline: string;
  gridLines: TrendGridLine[];
}

/** Space around the plotting area, in the same units as the drawing, for the axis labels and the end of the line. */
export interface TrendMargins {
  left: number;
  right: number;
  top: number;
  bottom: number;
}

/**
 * The default drawing: 600 by 240 units, scaled to the width of the page. The axis labels are HTML beside the drawing, not text inside it,
 * because text inside a scaled drawing shrinks below a readable size on a phone. The margins only keep a marker from being cut off at the edge.
 */
export const TREND_SIZE = { width: 600, height: 300 } as const;
export const TREND_MARGINS: TrendMargins = { left: 12, right: 14, top: 14, bottom: 14 };

/**
 * Places the results on the trend. The horizontal axis is the order the exams were sat, spaced evenly, not the calendar: an
 * exam taken a month after another is not drawn a month further along, so a run of exams reads as a run of exams.
 * The vertical axis runs from 0 to 100 percent of the marks, widened only when a result went below zero or above 100, so the
 * line is never clipped and the scale never hides a bad sitting.
 *
 * @param percents Each result's share of the marks, in the order sat; null for a result with no marks, which is kept in its place but not drawn.
 * @param size The drawing's width and height.
 * @param margins Space kept around the plotting area for the labels.
 * @returns The drawn points, the line through them and the gridlines.
 */
export function plotTrend(
  percents: readonly (number | null)[],
  size: { width: number; height: number } = TREND_SIZE,
  margins: TrendMargins = TREND_MARGINS,
): TrendPlot {
  const drawn = percents
    .map((percent, index) => ({ index, percent }))
    .filter((entry): entry is { index: number; percent: number } => entry.percent !== null);

  const plotWidth = size.width - margins.left - margins.right;
  const plotHeight = size.height - margins.top - margins.bottom;
  const values = drawn.map((entry) => entry.percent);
  const low = Math.min(0, ...values);
  const high = Math.max(100, ...values);
  const count = percents.length;

  const xOf = (index: number): number =>
    count <= 1 ? margins.left + plotWidth / 2 : margins.left + (index / (count - 1)) * plotWidth;
  const yOf = (percent: number): number => margins.top + ((high - percent) / (high - low)) * plotHeight;

  const points = drawn.map((entry) => ({
    index: entry.index,
    x: round(xOf(entry.index)),
    y: round(yOf(entry.percent)),
    percent: entry.percent,
  }));

  // Gridlines stay at 0, 50 and 100 whatever the scale. A line for the lowest value would sit on top of the 0 label whenever that value is
  // only a little below zero, and the value of a point below zero is shown by its end label and tooltip, not by a gridline.
  const gridLines = [100, 50, 0].map((percent) => ({ percent, y: round(yOf(percent)) }));

  return {
    points,
    polyline: points.length < 2 ? '' : points.map((p) => `${p.x},${p.y}`).join(' '),
    gridLines,
  };
}

function round(value: number): number {
  return Math.round(value * 100) / 100;
}
