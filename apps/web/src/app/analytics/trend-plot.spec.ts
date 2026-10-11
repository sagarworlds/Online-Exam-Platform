import { plotTrend, TREND_MARGINS, TREND_SIZE } from './trend-plot';

describe('plotTrend', () => {
  const plotLeft = TREND_MARGINS.left;
  const plotRight = TREND_SIZE.width - TREND_MARGINS.right;
  const plotTop = TREND_MARGINS.top;
  const plotBottom = TREND_SIZE.height - TREND_MARGINS.bottom;

  it('spaces the results evenly by the order they were sat, not by date', () => {
    const plot = plotTrend([10, 20, 30]);

    expect(plot.points.map((p) => p.x)).toEqual([plotLeft, (plotLeft + plotRight) / 2, plotRight]);
  });

  it('puts 0 percent on the bottom edge and 100 percent on the top edge of the plotting area', () => {
    const plot = plotTrend([0, 100]);

    expect(plot.points[0].y).toBe(plotBottom);
    expect(plot.points[1].y).toBe(plotTop);
  });

  it('centres a single result rather than pinning it to an edge', () => {
    const plot = plotTrend([50]);

    expect(plot.points).toHaveLength(1);
    expect(plot.points[0].x).toBe((plotLeft + plotRight) / 2);
    // A single point has no line to draw.
    expect(plot.polyline).toBe('');
  });

  it('joins the drawn points in order as one polyline', () => {
    const plot = plotTrend([0, 100]);

    expect(plot.polyline).toBe(`${plotLeft},${plotBottom} ${plotRight},${plotTop}`);
  });

  it('keeps a result with no marks in its place without drawing a point for it', () => {
    const plot = plotTrend([40, null, 80]);

    expect(plot.points.map((p) => p.index)).toEqual([0, 2]);
    expect(plot.points[1].x).toBe(plotRight);
  });

  it('widens the scale below zero so a negative result is not clipped off the chart', () => {
    const plot = plotTrend([-20, 50]);

    const lowest = plot.points[0];
    expect(lowest.y).toBeLessThanOrEqual(plotBottom);
    expect(lowest.y).toBeGreaterThan(plot.gridLines[2].y);
    expect(plot.gridLines.map((g) => g.percent)).toEqual([100, 50, 0]);
  });

  it('draws gridlines at 0, 50 and 100 percent on an ordinary scale, highest first', () => {
    const plot = plotTrend([30]);

    expect(plot.gridLines.map((g) => g.percent)).toEqual([100, 50, 0]);
    expect(plot.gridLines[0].y).toBe(plotTop);
    expect(plot.gridLines[2].y).toBe(plotBottom);
  });

  it('draws nothing for an empty trend', () => {
    const plot = plotTrend([]);

    expect(plot.points).toEqual([]);
    expect(plot.polyline).toBe('');
  });
});
