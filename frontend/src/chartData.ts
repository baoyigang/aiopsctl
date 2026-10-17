import type { ChartResult } from './types';

/** Null is a visual gap marker, never an interpolated or counted sample. */
function withGapMarkers(chart: ChartResult): ChartResult['points'] {
  const interval = chart.intervalSeconds ? chart.intervalSeconds * 1000 : 0;
  if (!interval || !Number.isFinite(interval) || interval < 0) return chart.points;
  const points: ChartResult['points'] = [];
  chart.points.forEach((point, index) => {
    const previous = chart.points[index - 1];
    if (previous) {
      const previousAt = Date.parse(previous.timestamp);
      const currentAt = Date.parse(point.timestamp);
      if (Number.isFinite(previousAt) && currentAt - previousAt > interval + 1) {
        points.push({
          timestamp: new Date(previousAt + interval).toISOString(),
          value: null,
          comparisonValue: null,
        });
      }
    }
    points.push(point);
  });
  return points;
}

export function chartLabels(chart: ChartResult): string[] {
  return withGapMarkers(chart).map((point) => point.timestamp.slice(5, 16).replace('T', ' '));
}

export function chartSeries(chart: ChartResult, comparison = 'none') {
  const points = withGapMarkers(chart);
  const series = [{ name: '本期', color: '#9270d7', values: points.map((point) => point.value) }];
  if (comparison !== 'none')
    series.push({
      name: comparison === 'yesterday' ? '昨日同期' : '上周同期',
      color: '#e8bd46',
      values: points.map((point) => point.comparisonValue ?? null),
    });
  return series;
}

export function sourceLabel(mode: string | undefined): string {
  return mode === 'demo'
    ? '合成演示'
    : mode === 'mixed'
      ? '混合来源 · 分图标识'
      : mode === 'es'
        ? 'ES 只读结果 · 未经企业实测'
        : '来源待确认';
}
