<script setup lang="ts">
import { computed, getCurrentInstance, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { chartColor, type ChartSeries, type ObservationChartKind } from './chart-types'

const props = withDefaults(defineProps<{
  title: string
  labels: string[]
  series: ChartSeries[]
  kind?: ObservationChartKind
  unit?: string
  loading?: boolean
  error?: string
}>(), { kind: 'line', unit: '', loading: false, error: '' })

const instanceId = `hc-chart-${getCurrentInstance()?.uid ?? 0}`
const hidden = ref<Set<number>>(new Set())
const mode = ref<'compare' | 'stats'>('compare')
const hovered = ref<number | null>(null)
const isFullscreen = ref(false)
const panel = ref<HTMLElement | null>(null)
const stage = ref<HTMLDivElement | null>(null)
const fullscreenButton = ref<HTMLButtonElement | null>(null)
const measuredWidth = ref(760)
let resizeObserver: ResizeObserver | undefined
let resizeFrame = 0
let previousOverflow = ''
let previousFocus: HTMLElement | null = null

const width = computed(() => Math.max(320, measuredWidth.value))
const height = 300
const plot = { left: 58, right: 22, top: 26, bottom: 47 }
const plotWidth = computed(() => width.value - plot.left - plot.right)
const plotHeight = height - plot.top - plot.bottom
const visible = computed(() => props.series.map((series, index) => ({
  ...series, index, color: chartColor(series.color, index),
})).filter(series => !hidden.value.has(series.index)))
const count = computed(() => Math.max(0, props.labels.length, ...props.series.map(series => series.values.length)))
const finiteValues = computed(() => visible.value.flatMap(series => series.values.filter((value): value is number => value !== null && Number.isFinite(value))))
const hasData = computed(() => count.value > 0 && finiteValues.value.length > 0)
const mismatch = computed(() => props.series.some(series => series.values.length !== props.labels.length))
const negativeFunnel = computed(() => props.kind === 'funnel' && finiteValues.value.some(value => value < 0))
const label = (index: number) => props.labels[index] || `第 ${index + 1} 项`
const shortLabel = (value: string, max = 13) => value.length > max ? `${value.slice(0, max - 1)}…` : value
const numberFormat = new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 2 })
const format = (value: number | null | undefined, withUnit = true): string =>
  value === null || value === undefined || !Number.isFinite(value)
    ? '无数据'
    : `${numberFormat.format(value)}${withUnit && props.unit ? ` ${props.unit}` : ''}`
const axisFormat = (value: number) => {
  const absolute = Math.abs(value)
  if (absolute >= 100000000) return `${numberFormat.format(value / 100000000)}亿`
  if (absolute >= 10000) return `${numberFormat.format(value / 10000)}万`
  if (absolute >= 1000) return `${numberFormat.format(value / 1000)}k`
  return numberFormat.format(value)
}
const domain = computed(() => {
  const values = finiteValues.value
  let min = Math.min(0, ...values)
  let max = Math.max(0, ...values)
  if (min === max) max = min + 1
  const padding = (max - min) * .08
  if (max > 0) max += padding
  if (min < 0) min -= padding
  return { min, max }
})
const y = (value: number) => plot.top + (domain.value.max - value) / (domain.value.max - domain.value.min) * plotHeight
const x = (index: number) => props.kind === 'bar'
  ? plot.left + (index + .5) / Math.max(1, count.value) * plotWidth.value
  : plot.left + (count.value <= 1 ? plotWidth.value / 2 : index / (count.value - 1) * plotWidth.value)
const yTicks = computed(() => Array.from({ length: 5 }, (_, index) => {
  const value = domain.value.min + (domain.value.max - domain.value.min) * index / 4
  return { value, y: y(value) }
}))
const xTicks = computed(() => {
  if (!count.value) return []
  const every = Math.max(1, Math.ceil(count.value / (width.value < 520 ? 4 : 7)))
  return Array.from({ length: count.value }, (_, index) => index)
    .filter(index => index % every === 0 || index === count.value - 1)
    .map(index => ({ index, x: x(index), label: shortLabel(label(index), width.value < 520 ? 8 : 12) }))
})
const linePath = (values: (number | null)[]) => {
  let hasPrevious = false
  return values.map((value, index) => {
    if (value === null || !Number.isFinite(value)) {
      hasPrevious = false
      return ''
    }
    const command = hasPrevious ? 'L' : 'M'
    hasPrevious = true
    return `${command}${x(index)},${y(value)}`
  }).join(' ')
}
const bars = computed(() => {
  const groupWidth = plotWidth.value / Math.max(1, count.value) * .72
  const barWidth = Math.max(.7, Math.min(34, groupWidth / Math.max(1, visible.value.length) - 3))
  return visible.value.flatMap((series, seriesIndex) => series.values.flatMap((value, index) => {
    if (value === null || !Number.isFinite(value)) return []
    const seriesWidth = groupWidth / Math.max(1, visible.value.length)
    return [{
      key: `${series.index}-${index}`, index, name: series.name, color: series.color, value,
      x: x(index) - groupWidth / 2 + seriesWidth * seriesIndex + (seriesWidth - barWidth) / 2,
      y: Math.min(y(0), y(value)), width: barWidth, height: Math.max(1, Math.abs(y(value) - y(0))),
    }]
  }))
})
const funnelColumns = computed(() => {
  const max = Math.max(1, ...finiteValues.value.filter(value => value >= 0))
  const columnWidth = plotWidth.value / Math.max(1, visible.value.length)
  const rowHeight = plotHeight / Math.max(1, count.value)
  return visible.value.map((series, seriesIndex) => {
    const center = plot.left + (seriesIndex + .5) * columnWidth
    const maxWidth = Math.max(1, columnWidth - 20)
    return {
      ...series, center,
      segments: series.values.flatMap((value, index) => {
        if (value === null || !Number.isFinite(value) || value < 0) return []
        const next = series.values[index + 1]
        const nextValue = next !== null && next !== undefined && Number.isFinite(next) && next >= 0 ? next : value
        const topWidth = value / max * maxWidth
        const bottomWidth = nextValue / max * maxWidth
        const top = plot.top + index * rowHeight + 5
        const bottom = plot.top + (index + 1) * rowHeight - 2
        return [{ index, value, center, textY: (top + bottom) / 2 + 4, textFits: Math.min(topWidth, bottomWidth) > 62,
          points: `${center - topWidth / 2},${top} ${center + topWidth / 2},${top} ${center + bottomWidth / 2},${bottom} ${center - bottomWidth / 2},${bottom}` }]
      }),
    }
  })
})
const statistics = computed(() => visible.value.map(series => {
  const values = series.values.filter((value): value is number => value !== null && Number.isFinite(value))
  const sum = values.reduce((total, value) => total + value, 0)
  return { ...series, count: values.length, sum: values.length ? sum : null,
    min: values.length ? Math.min(...values) : null, max: values.length ? Math.max(...values) : null,
    average: values.length ? sum / values.length : null, last: values[values.length - 1] ?? null }
}))
const tooltipRows = computed(() => hovered.value === null ? [] : visible.value.map(series => ({
  index: series.index, name: series.name, color: series.color, value: series.values[hovered.value!] ?? null,
})))
const tooltipLeft = computed(() => props.kind === 'funnel' ? 70 : Math.max(24, Math.min(76, x(hovered.value ?? 0) / width.value * 100)))
const accessibleSummary = computed(() => `${props.title}，${props.kind === 'bar' ? '柱状图' : props.kind === 'funnel' ? '漏斗图' : '折线图'}，${count.value} 个数据位置，${visible.value.length} 个可见系列。使用左右方向键查看数据。`)

function toggleSeries(index: number) {
  const next = new Set(hidden.value)
  if (next.has(index)) next.delete(index)
  else next.add(index)
  hidden.value = next
}
function onPointerMove(event: PointerEvent) {
  if (!hasData.value) return
  const svg = event.currentTarget as SVGSVGElement
  const matrix = svg.getScreenCTM()
  if (!matrix) return
  const screenPoint = svg.createSVGPoint()
  screenPoint.x = event.clientX
  screenPoint.y = event.clientY
  // SVG may have letterboxing on narrow cards or in a fullscreen panel.
  const { x: svgX, y: svgY } = screenPoint.matrixTransform(matrix.inverse())
  if (svgX < plot.left || svgX > width.value - plot.right || svgY < plot.top || svgY > height - plot.bottom) {
    hovered.value = null
    return
  }
  if (props.kind === 'funnel') hovered.value = Math.min(count.value - 1, Math.floor((svgY - plot.top) / plotHeight * count.value))
  else if (props.kind === 'bar') hovered.value = Math.min(count.value - 1, Math.floor((svgX - plot.left) / plotWidth.value * count.value))
  else hovered.value = count.value <= 1 ? 0 : Math.round((svgX - plot.left) / plotWidth.value * (count.value - 1))
}
function onChartKey(event: KeyboardEvent) {
  if (!hasData.value || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return
  event.preventDefault()
  if (event.key === 'Home') hovered.value = 0
  else if (event.key === 'End') hovered.value = count.value - 1
  else hovered.value = Math.max(0, Math.min(count.value - 1, (hovered.value ?? 0) + (['ArrowLeft', 'ArrowUp'].includes(event.key) ? -1 : 1)))
}
function onPanelKey(event: KeyboardEvent) {
  if (!isFullscreen.value) return
  if (event.key === 'Escape') {
    event.preventDefault()
    isFullscreen.value = false
  }
  if (event.key !== 'Tab') return
  const focusable = panel.value?.querySelectorAll<HTMLElement>('button:not([disabled]), [tabindex="0"]')
  if (!focusable?.length) return
  const first = focusable[0]!
  const last = focusable[focusable.length - 1]!
  if (event.shiftKey && (document.activeElement === first || document.activeElement === panel.value)) {
    event.preventDefault(); last.focus()
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault(); first.focus()
  }
}
watch(isFullscreen, async value => {
  if (typeof document === 'undefined') return
  if (value) {
    previousFocus = document.activeElement as HTMLElement | null
    previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    await nextTick()
    panel.value?.focus()
  } else {
    document.body.style.overflow = previousOverflow
    await nextTick()
    ;(previousFocus ?? fullscreenButton.value)?.focus()
  }
})
watch(() => props.series, () => {
  hidden.value = new Set([...hidden.value].filter(index => index < props.series.length))
  hovered.value = null
})
watch(() => [props.labels, props.kind, props.loading, props.error], () => { hovered.value = null })
watch(stage, element => {
  resizeObserver?.disconnect()
  cancelAnimationFrame(resizeFrame)
  if (!element || typeof ResizeObserver === 'undefined') return
  const measure = () => {
    const nextWidth = Math.round(element.getBoundingClientRect().width) || 760
    if (measuredWidth.value !== nextWidth) measuredWidth.value = nextWidth
  }
  measure()
  resizeObserver = new ResizeObserver(() => {
    cancelAnimationFrame(resizeFrame)
    resizeFrame = requestAnimationFrame(measure)
  })
  resizeObserver.observe(element)
}, { flush: 'post' })
onBeforeUnmount(() => {
  resizeObserver?.disconnect()
  cancelAnimationFrame(resizeFrame)
  if (isFullscreen.value && typeof document !== 'undefined') document.body.style.overflow = previousOverflow
})
</script>

<template>
  <Teleport to="body" :disabled="!isFullscreen">
    <div class="hc-chart-host" :class="{ 'hc-chart-host--fullscreen': isFullscreen }" @click.self="isFullscreen = false">
      <section ref="panel" class="hc-chart" :class="{ 'hc-chart--fullscreen': isFullscreen }" :role="isFullscreen ? 'dialog' : 'region'" :aria-modal="isFullscreen ? true : undefined" :aria-labelledby="`${instanceId}-title`" :aria-busy="loading" :tabindex="isFullscreen ? -1 : undefined" @keydown="onPanelKey">
        <header class="hc-chart-header">
          <div class="hc-chart-heading">
            <span class="hc-chart-mark" aria-hidden="true"></span>
            <h3 :id="`${instanceId}-title`">{{ title }}</h3>
            <span v-if="unit" class="hc-chart-unit">{{ unit }}</span>
          </div>
          <div class="hc-chart-actions">
            <div class="hc-chart-tabs" role="group" aria-label="图表显示模式">
              <button type="button" :aria-pressed="mode === 'compare'" :class="{ 'hc-chart-active': mode === 'compare' }" @click="mode = 'compare'">对比</button>
              <button type="button" :aria-pressed="mode === 'stats'" :class="{ 'hc-chart-active': mode === 'stats' }" @click="mode = 'stats'">统计</button>
            </div>
            <button ref="fullscreenButton" type="button" class="hc-chart-fullscreen" :aria-label="isFullscreen ? '退出全屏' : '全屏查看图表'" :title="isFullscreen ? '退出全屏（Esc）' : '全屏查看'" @click="isFullscreen = !isFullscreen">
              <svg v-if="!isFullscreen" viewBox="0 0 20 20" fill="none" aria-hidden="true"><path d="M7 3H3v4m10-4h4v4M3 13v4h4m10-4v4h-4" /></svg>
              <svg v-else viewBox="0 0 20 20" fill="none" aria-hidden="true"><path d="m5 5 10 10M15 5 5 15" /></svg>
            </button>
          </div>
        </header>

        <div v-if="series.length" class="hc-chart-legend" aria-label="系列显示设置">
          <button v-for="(seriesItem, index) in series" :key="`${seriesItem.name}-${index}`" type="button" :class="{ 'hc-chart-legend--hidden': hidden.has(index) }" :aria-pressed="!hidden.has(index)" @click="toggleSeries(index)">
            <span :style="{ backgroundColor: chartColor(seriesItem.color, index) }" aria-hidden="true"></span>{{ seriesItem.name }}
          </button>
          <button v-if="hidden.size" type="button" class="hc-chart-restore" @click="hidden = new Set()">显示全部</button>
        </div>

        <div v-if="loading" class="hc-chart-state" role="status"><span class="hc-chart-spinner" aria-hidden="true"></span><strong>正在加载图表</strong><p>请稍候</p></div>
        <div v-else-if="error" class="hc-chart-state hc-chart-state--error" role="alert"><span class="hc-chart-state-icon" aria-hidden="true">!</span><strong>图表暂不可用</strong><p>{{ error }}</p></div>
        <div v-else-if="!hasData" class="hc-chart-state" role="status">
          <svg class="hc-chart-empty-icon" viewBox="0 0 48 48" fill="none" aria-hidden="true"><path d="M8 8v32h33M15 31l9-10 7 5 10-13" /><circle cx="24" cy="21" r="3" /></svg>
          <strong>{{ series.length && !visible.length ? '当前系列均已隐藏' : '暂无可展示的数据' }}</strong><p>{{ series.length && !visible.length ? '点击图例可重新显示系列' : '请调整筛选条件或选择其他时间范围' }}</p>
        </div>

        <template v-else>
          <div v-if="mode === 'compare'" ref="stage" class="hc-chart-stage">
            <svg class="hc-chart-svg" :viewBox="`0 0 ${width} ${height}`" role="img" :aria-label="accessibleSummary" :aria-describedby="hovered !== null ? `${instanceId}-tooltip` : undefined" tabindex="0" @pointermove="onPointerMove" @pointerleave="hovered = null" @focus="hovered = 0" @blur="hovered = null" @keydown="onChartKey">
              <title>{{ accessibleSummary }}</title>
              <template v-if="kind !== 'funnel'">
                <g class="hc-chart-grid"><line v-for="tick in yTicks" :key="tick.value" :x1="plot.left" :x2="width - plot.right" :y1="tick.y" :y2="tick.y" /></g>
                <g class="hc-chart-axis"><text v-for="tick in yTicks" :key="tick.value" :x="plot.left - 12" :y="tick.y + 4" text-anchor="end">{{ axisFormat(tick.value) }}</text><text v-for="tick in xTicks" :key="tick.index" :x="tick.x" :y="height - 19" :text-anchor="tick.index === 0 && count > 1 ? 'start' : tick.index === count - 1 && count > 1 ? 'end' : 'middle'"><title>{{ label(tick.index) }}</title>{{ tick.label }}</text></g>
                <line v-if="domain.min < 0" class="hc-chart-zero" :x1="plot.left" :x2="width - plot.right" :y1="y(0)" :y2="y(0)" />
              </template>

              <g v-if="kind === 'line'">
                <template v-for="seriesItem in visible" :key="seriesItem.index">
                  <path :d="linePath(seriesItem.values)" fill="none" :stroke="seriesItem.color" stroke-width="2.8" stroke-linejoin="round" stroke-linecap="round" vector-effect="non-scaling-stroke" />
                  <template v-for="(value, index) in seriesItem.values" :key="index">
                    <circle v-if="value !== null && Number.isFinite(value) && (count <= 12 || hovered === index)" :cx="x(index)" :cy="y(value)" :r="hovered === index ? 4.5 : 3" fill="white" :stroke="seriesItem.color" stroke-width="2" />
                  </template>
                </template>
              </g>
              <g v-else-if="kind === 'bar'">
                <rect v-for="bar in bars" :key="bar.key" :x="bar.x" :y="bar.y" :width="bar.width" :height="bar.height" :fill="bar.color" :opacity="hovered !== null && hovered !== bar.index ? .42 : .9" rx="2"><title>{{ label(bar.index) }} · {{ bar.name }}：{{ format(bar.value) }}</title></rect>
              </g>
              <template v-else>
                <g class="hc-chart-axis"><text v-for="(_, index) in count" :key="index" :x="plot.left - 10" :y="plot.top + (index + .5) / count * plotHeight + 4" text-anchor="end"><title>{{ label(index) }}</title>{{ shortLabel(label(index), 5) }}</text></g>
                <g v-for="column in funnelColumns" :key="column.index">
                  <text class="hc-chart-funnel-name" :x="column.center" :y="plot.top - 8" text-anchor="middle">{{ shortLabel(column.name, 18) }}</text>
                  <g v-for="segment in column.segments" :key="segment.index">
                    <polygon :points="segment.points" :fill="column.color" :opacity="hovered !== null && hovered !== segment.index ? .4 : .88"><title>{{ label(segment.index) }} · {{ column.name }}：{{ format(segment.value) }}</title></polygon>
                    <text v-if="segment.textFits && count <= 10" class="hc-chart-funnel-value" :x="segment.center" :y="segment.textY" text-anchor="middle">{{ format(segment.value, false) }}</text>
                  </g>
                </g>
              </template>

              <line v-if="hovered !== null && kind !== 'funnel'" class="hc-chart-crosshair" :x1="x(hovered)" :x2="x(hovered)" :y1="plot.top" :y2="height - plot.bottom" />
            </svg>
            <div v-if="hovered !== null" :id="`${instanceId}-tooltip`" class="hc-chart-tooltip" role="tooltip" :style="{ left: `${tooltipLeft}%` }">
              <strong>{{ label(hovered) }}</strong>
              <div v-for="row in tooltipRows" :key="row.index"><span class="hc-chart-tooltip-dot" :style="{ backgroundColor: row.color }"></span><span>{{ row.name }}</span><b>{{ format(row.value) }}</b></div>
            </div>
          </div>
          <div v-else class="hc-chart-statistics">
            <table><caption class="hc-chart-sr-only">{{ title }}，可见系列的有效数据统计</caption><thead><tr><th scope="col">系列</th><th scope="col">有效点</th><th scope="col">最新有效值</th><th scope="col">平均值</th><th scope="col">最小值</th><th scope="col">最大值</th><th scope="col">合计</th></tr></thead><tbody><tr v-for="row in statistics" :key="row.index"><th scope="row"><span class="hc-chart-table-dot" :style="{ backgroundColor: row.color }"></span>{{ row.name }}</th><td>{{ row.count }}</td><td>{{ format(row.last) }}</td><td>{{ format(row.average) }}</td><td>{{ format(row.min) }}</td><td>{{ format(row.max) }}</td><td>{{ format(row.sum) }}</td></tr></tbody></table>
            <p>统计仅基于当前传入的有效数值，空值不计入。合计为数值相加，是否适用取决于指标含义。</p>
          </div>
          <footer class="hc-chart-footer"><span>{{ count }} 个数据位置 · {{ visible.length }} 个可见系列</span><span v-if="mismatch">缺失标签使用序号，缺失值不补零</span><span v-if="negativeFunnel">漏斗仅展示非负值；负值保留在统计中</span><span v-if="mode === 'compare'" class="hc-chart-key-hint">悬停或使用方向键查看数值</span></footer>
        </template>
      </section>
    </div>
  </Teleport>
</template>

<style scoped>
.hc-chart-host { min-width: 0; width: 100%; }
.hc-chart { box-sizing: border-box; overflow: hidden; width: 100%; background: #fff; border: 1px solid #eceaf1; border-radius: 14px; color: #302c41; box-shadow: 0 3px 14px #31225103; font-family: inherit; }
.hc-chart *, .hc-chart *::before, .hc-chart *::after { box-sizing: border-box; }
.hc-chart button { font: inherit; cursor: pointer; }
.hc-chart button:focus-visible, .hc-chart-svg:focus-visible { outline: 2px solid #7c58df; outline-offset: 3px; }
.hc-chart-header { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 21px 23px 14px; }
.hc-chart-heading, .hc-chart-actions { display: flex; align-items: center; min-width: 0; gap: 10px; }
.hc-chart-heading h3 { margin: 0; font-size: 14px; line-height: 1.5; font-weight: 650; overflow-wrap: anywhere; }
.hc-chart-mark { width: 4px; height: 15px; background: #7c58df; border-radius: 3px; flex-shrink: 0; }
.hc-chart-unit { color: #a39cab; font-size: 11px; white-space: nowrap; }
.hc-chart-tabs { display: flex; gap: 2px; padding: 3px; background: #f6f5f9; border-radius: 6px; }
.hc-chart-tabs button { padding: 4px 11px; border: 0; border-radius: 4px; color: #96909f; background: transparent; font-size: 11px; line-height: 1.4; white-space: nowrap; }
.hc-chart-tabs button.hc-chart-active { background: white; color: #7c58df; box-shadow: 0 1px 4px #3726530d; font-weight: 600; }
.hc-chart-fullscreen { display: grid; place-items: center; width: 28px; height: 28px; border: 1px solid #eeecf3; border-radius: 6px; background: white; color: #9c95a9; }
.hc-chart-fullscreen:hover { color: #7c58df; background: #faf8ff; }
.hc-chart-fullscreen svg { width: 16px; height: 16px; stroke: currentColor; stroke-width: 1.4; stroke-linecap: round; stroke-linejoin: round; }
.hc-chart-legend { display: flex; flex-wrap: wrap; gap: 7px 17px; min-height: 29px; padding: 0 24px 10px; }
.hc-chart-legend button { display: inline-flex; align-items: center; gap: 6px; padding: 2px 0; border: 0; color: #81798e; background: transparent; font-size: 11px; line-height: 1.5; }
.hc-chart-legend button > span { display: inline-block; width: 7px; height: 7px; border-radius: 50%; flex-shrink: 0; }
.hc-chart-legend button.hc-chart-legend--hidden { opacity: .4; text-decoration: line-through; }
.hc-chart-legend button.hc-chart-restore { color: #7c58df; }
.hc-chart-stage { position: relative; margin: 0 15px; min-height: 200px; }
.hc-chart-svg { display: block; width: 100%; height: auto; max-height: 410px; overflow: visible; outline: none; }
.hc-chart-grid line { stroke: #eeedf3; stroke-width: 1; stroke-dasharray: 3 4; }
.hc-chart-axis text { fill: #a49dad; font-size: 10px; font-family: inherit; }
.hc-chart-zero { stroke: #cbc5d5; stroke-width: 1; }
.hc-chart-crosshair { stroke: #b6a8d7; stroke-width: 1; stroke-dasharray: 4 4; pointer-events: none; }
.hc-chart-funnel-name { fill: #8b829b; font-size: 10px; }
.hc-chart-funnel-value { fill: #302742; font-size: 11px; font-weight: 650; pointer-events: none; }
.hc-chart-tooltip { position: absolute; top: 14px; z-index: 2; min-width: 150px; max-width: min(300px, 90%); padding: 12px 14px; transform: translateX(-50%); border: 1px solid #eeebf5; border-radius: 9px; background: #fffffffa; box-shadow: 0 7px 25px #30234414; font-size: 11px; pointer-events: none; }
.hc-chart-tooltip > strong { display: block; margin-bottom: 8px; overflow-wrap: anywhere; color: #514861; font-size: 11px; }
.hc-chart-tooltip > div { display: flex; align-items: center; gap: 6px; margin-top: 5px; color: #8b8298; }
.hc-chart-tooltip > div > span:nth-child(2) { overflow-wrap: anywhere; }
.hc-chart-tooltip b { margin-left: auto; padding-left: 12px; white-space: nowrap; color: #42394f; font-weight: 600; }
.hc-chart-tooltip-dot, .hc-chart-table-dot { display: inline-block; width: 6px; height: 6px; flex-shrink: 0; border-radius: 50%; }
.hc-chart-footer { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 4px 16px; padding: 0 24px 15px; color: #b0a9ba; font-size: 10px; line-height: 1.6; }
.hc-chart-state { display: flex; min-height: 270px; flex-direction: column; justify-content: center; align-items: center; gap: 10px; padding: 26px; text-align: center; color: #b3acbf; }
.hc-chart-state strong { color: #82768f; font-size: 13px; font-weight: 500; }
.hc-chart-state p { max-width: 480px; margin: 0; color: #aba2b6; font-size: 12px; line-height: 1.7; overflow-wrap: anywhere; white-space: pre-wrap; }
.hc-chart-empty-icon { width: 43px; height: 43px; margin-bottom: 3px; stroke: #c4b7e3; stroke-width: 1.6; stroke-linejoin: round; stroke-linecap: round; }
.hc-chart-state-icon { display: grid; place-items: center; width: 33px; height: 33px; margin-bottom: 4px; border: 1px solid #e9b1b8; border-radius: 50%; color: #ce7380; font-size: 21px; }
.hc-chart-state--error strong { color: #b76d79; }
.hc-chart-spinner { width: 26px; height: 26px; border: 2px solid #eee8fa; border-top-color: #8a68df; border-radius: 50%; animation: hc-chart-spin .8s linear infinite; }
.hc-chart-statistics { overflow-x: auto; min-height: 268px; padding: 15px 24px 20px; }
.hc-chart-statistics table { width: 100%; border-collapse: collapse; white-space: nowrap; font-size: 11px; text-align: right; }
.hc-chart-statistics th, .hc-chart-statistics td { padding: 13px 12px; border-bottom: 1px solid #f0edf5; font-weight: 400; }
.hc-chart-statistics thead th { background: #f9f8fc; color: #a196ad; font-size: 10px; }
.hc-chart-statistics th:first-child { text-align: left; padding-left: 10px; }
.hc-chart-statistics tbody th { color: #776782; }
.hc-chart-statistics td { color: #685b78; font-variant-numeric: tabular-nums; }
.hc-chart-table-dot { margin-right: 7px; vertical-align: 1px; }
.hc-chart-statistics p { margin: 14px 0 0; color: #aea5b9; font-size: 10px; line-height: 1.7; }
.hc-chart-host--fullscreen { position: fixed; inset: 0; z-index: 10000; display: grid; place-items: center; padding: 5vh 4vw; background: #21182e70; backdrop-filter: blur(3px); }
.hc-chart--fullscreen { max-width: 1280px; max-height: 90vh; overflow: auto; box-shadow: 0 25px 100px #1f153c30; outline: none; }
.hc-chart--fullscreen .hc-chart-svg { max-height: 65vh; }
.hc-chart--fullscreen .hc-chart-stage { margin: 10px 22px; }
.hc-chart--fullscreen .hc-chart-statistics { min-height: 400px; }
.hc-chart-sr-only { position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip: rect(0, 0, 0, 0); white-space: nowrap; border: 0; }
@keyframes hc-chart-spin { to { transform: rotate(360deg); } }
@media (prefers-reduced-motion: reduce) { .hc-chart-spinner { animation: none; } }
@media (max-width: 560px) {
  .hc-chart-header { padding: 17px 15px 12px; align-items: flex-start; }
  .hc-chart-heading { gap: 7px; flex-wrap: wrap; }
  .hc-chart-heading h3 { font-size: 13px; }
  .hc-chart-unit { display: none; }
  .hc-chart-actions { gap: 6px; }
  .hc-chart-tabs button { padding: 4px 8px; }
  .hc-chart-legend { padding: 0 16px 8px; gap: 5px 12px; }
  .hc-chart-stage { margin: 0 5px; min-height: 160px; }
  .hc-chart-svg { min-height: 180px; }
  .hc-chart-tooltip { top: 8px; min-width: 130px; padding: 9px 10px; }
  .hc-chart-footer { padding: 0 16px 13px; }
  .hc-chart-key-hint { display: none; }
  .hc-chart-host--fullscreen { padding: 16px 10px; }
  .hc-chart--fullscreen { max-height: calc(100dvh - 32px); }
  .hc-chart--fullscreen .hc-chart-stage { margin: 10px 4px; }
  .hc-chart-state { min-height: 210px; }
  .hc-chart-statistics { padding: 10px 15px 15px; min-height: 210px; }
}
</style>
