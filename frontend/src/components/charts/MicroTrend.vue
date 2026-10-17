<script setup lang="ts">
import { computed, getCurrentInstance } from 'vue'
import { chartColor } from './chart-types'

const props = withDefaults(defineProps<{
  values: number[]
  color?: string
  height?: number
}>(), { color: '#7c58df', height: 36 })

const gradientId = `hc-chart-trend-${getCurrentInstance()?.uid ?? 0}`
const paint = computed(() => chartColor(props.color))
const safeHeight = computed(() => Math.max(16, Math.min(200, props.height)))
const finiteValues = computed(() => props.values.filter(Number.isFinite))
const summary = computed(() => {
  const values = finiteValues.value
  if (!values.length) return '暂无趋势数据'
  return `趋势，共 ${values.length} 个有效值，最小 ${Math.min(...values)}，最大 ${Math.max(...values)}，最新 ${values[values.length - 1]}`
})
const geometry = computed(() => {
  const values = finiteValues.value
  if (!values.length) return { line: '', area: '', end: null }
  const min = Math.min(...values)
  const max = Math.max(...values)
  const range = max - min || 1
  const bottom = 44
  const segments: { x: number; y: number }[][] = []
  let current: { x: number; y: number }[] = []
  props.values.forEach((value, index) => {
    if (!Number.isFinite(value)) {
      if (current.length) segments.push(current)
      current = []
      return
    }
    current.push({
      x: props.values.length === 1 ? 80 : 3 + index / (props.values.length - 1) * 154,
      y: max === min ? 24 : 5 + (1 - (value - min) / range) * 34,
    })
  })
  if (current.length) segments.push(current)
  const line = segments.map(segment => segment.map((point, index) => `${index ? 'L' : 'M'}${point.x},${point.y}`).join(' ')).join(' ')
  const area = segments.map(segment => {
    const first = segment[0]!
    const last = segment[segment.length - 1]!
    return `M${first.x},${bottom} ${segment.map(point => `L${point.x},${point.y}`).join(' ')} L${last.x},${bottom} Z`
  }).join(' ')
  const lastSegment = segments[segments.length - 1]!
  return { line, area, end: lastSegment[lastSegment.length - 1] ?? null }
})
</script>

<template>
  <svg class="hc-chart-micro" viewBox="0 0 160 48" preserveAspectRatio="none" :style="{ height: `${safeHeight}px` }" role="img" :aria-label="summary">
    <title>{{ summary }}</title>
    <defs>
      <linearGradient :id="gradientId" x1="0" y1="0" x2="0" y2="1">
        <stop offset="0%" :stop-color="paint" stop-opacity=".22" />
        <stop offset="100%" :stop-color="paint" stop-opacity="0" />
      </linearGradient>
    </defs>
    <path v-if="geometry.area" :d="geometry.area" :fill="`url(#${gradientId})`" />
    <path v-if="geometry.line" :d="geometry.line" fill="none" :stroke="paint" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" vector-effect="non-scaling-stroke" />
    <circle v-if="geometry.end" :cx="geometry.end.x" :cy="geometry.end.y" r="2.7" :fill="paint" />
    <path v-if="!geometry.line" d="M3 25H157" stroke="#e7e5ee" stroke-dasharray="3 4" fill="none" />
  </svg>
</template>

<style scoped>
.hc-chart-micro { display: block; width: 100%; min-width: 48px; overflow: visible; }
</style>
