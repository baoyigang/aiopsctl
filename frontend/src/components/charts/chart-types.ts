/** A data series supplied by a caller; charts never infer missing observations. */
export interface ChartSeries {
  name: string
  color: string
  values: (number | null)[]
}

export type ObservationChartKind = 'line' | 'bar' | 'funnel'

export const chartPalette = ['#7c58df', '#e8bd46', '#65cbb7', '#648cdb', '#de8cac']

/** Restrict SVG paint to a literal colour so data cannot reference remote assets. */
export function chartColor(value: string | undefined, index = 0): string {
  return value && /^#(?:[\da-f]{3,4}|[\da-f]{6}|[\da-f]{8})$/i.test(value)
    ? value
    : chartPalette[index % chartPalette.length]!
}
