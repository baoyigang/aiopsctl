<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { RouterLink, useRoute, useRouter } from 'vue-router';
import { api } from '../api';
import { app, inputIso, num, params, time, utcInput } from '../context';
import { chartLabels, chartSeries, sourceLabel } from '../chartData';
import type { BusinessDetail, ChartKind, Dashboard } from '../types';
import { useRequest } from '../composables/useRequest';
import EmptyState from '../components/EmptyState.vue';
import ObservationChart from '../components/charts/ObservationChart.vue';
const route = useRoute(),
  router = useRouter(),
  read = useRequest();
const detail = ref<BusinessDetail | null>(null),
  dashboard = ref<Dashboard | null>(null),
  aggregation = ref('avg'),
  comparison = ref('none'),
  from = ref(''),
  to = ref(''),
  preset = ref('12'),
  kinds = ref<Record<string, ChartKind>>({});
const currentId = computed(() => String(route.params.business ?? ''));
function setRange(hours: number) {
  const end = new Date(app.meta?.now ?? Date.now());
  to.value = utcInput(end.toISOString());
  from.value = utcInput(new Date(end.getTime() - hours * 3600000).toISOString());
  preset.value = String(hours);
}
async function load() {
  dashboard.value = null;
  await read.run(
    async (signal) => {
      const start = inputIso(from.value),
        end = inputIso(to.value);
      if (new Date(start) >= new Date(end)) throw new Error('结束时间须晚于开始时间。');
      return await Promise.all([
        api<BusinessDetail>(`/api/businesses/${encodeURIComponent(currentId.value)}`, { signal }),
        api<Dashboard>(
          `/api/businesses/${encodeURIComponent(currentId.value)}/dashboard?` +
            params({
              from: start,
              to: end,
              aggregation: aggregation.value,
              comparison: comparison.value,
            }),
          { signal },
        ),
      ]);
    },
    ([business, result]) => {
      app.spaceId = business.data.spaceId;
      detail.value = business;
      dashboard.value = result;
    },
  );
}
watch(
  currentId,
  () => {
    detail.value = null;
    setRange(12);
    load();
  },
  { immediate: true },
);
watch(
  () => app.spaceId,
  (value) => {
    if (detail.value && detail.value.data.spaceId !== value) router.push('/polaris');
  },
);
</script>
<template>
  <div class="page-header">
    <div>
      <div class="row" style="gap: 12px">
        <RouterLink class="muted small-text" to="/polaris">业务概览</RouterLink
        ><span class="muted">/</span>
        <h1>{{ detail?.data.name ?? '业务详情' }}</h1>
        <span class="badge" :class="dashboard?.mode === 'demo' ? 'green' : 'yellow'">{{
          sourceLabel(dashboard?.mode)
        }}</span>
      </div>
      <p v-if="detail">
        {{ detail.data.service }} · {{ detail.data.environment }} · 负责人 {{ detail.data.owner }} ·
        目标 {{ num(detail.data.targetAvailability, 3) }}%
      </p>
      <p v-else>查看业务关联指标的采样趋势与统计结果。</p>
    </div>
    <div class="page-actions">
      <RouterLink
        class="button secondary"
        :to="{ path: '/polaris/indicator', query: { businessId: currentId } }"
        >管理关联指标</RouterLink
      ><button class="button secondary" @click="load">↻ 刷新</button>
    </div>
  </div>
  <div class="panel" style="padding: 16px 18px; margin-bottom: 18px">
    <form
      class="filters"
      @submit.prevent="
        preset = 'custom';
        load();
      "
    >
      <span class="filter-label">统计窗口</span
      ><select
        v-model="preset"
        aria-label="快捷时间范围"
        @change="
          if (preset !== 'custom') {
            setRange(Number(preset));
            load();
          }
        "
      >
        <option value="1">最近 1 小时</option>
        <option value="6">最近 6 小时</option>
        <option value="12">最近 12 小时</option>
        <option value="24">最近 24 小时</option>
        <option value="168">最近 7 天</option>
        <option value="custom">自定义</option></select
      ><input
        v-model="from"
        data-testid="dashboard-from"
        type="datetime-local"
        aria-label="开始时间UTC"
        required
        style="width: 170px"
      /><span class="muted">—</span
      ><input
        v-model="to"
        data-testid="dashboard-to"
        type="datetime-local"
        aria-label="结束时间UTC"
        required
        style="width: 170px"
      /><span class="filter-separator"></span><span class="filter-label">聚合</span
      ><select v-model="aggregation" data-testid="dashboard-aggregation" @change="load">
        <option value="avg">平均值 avg</option>
        <option value="sum">求和 sum</option>
        <option value="raw">原始值 raw</option></select
      ><select
        v-model="comparison"
        data-testid="dashboard-comparison"
        aria-label="比较周期"
        @change="load"
      >
        <option value="none">不比较</option>
        <option value="yesterday">对比昨日</option>
        <option value="lastWeek">对比上周</option></select
      ><button class="button primary">应用</button
      ><button
        v-if="read.loading.value"
        type="button"
        class="button secondary"
        data-testid="dashboard-cancel"
        @click="read.cancel"
      >
        取消
      </button>
    </form>
  </div>
  <div class="notice">
    <span>◌</span
    ><span
      >来源：{{ sourceLabel(dashboard?.mode) }} · UTC ·
      {{ dashboard?.window.label ?? '所选时间范围' }}。{{
        dashboard?.mode === 'demo'
          ? '合成源按 5 分钟采样，avg / sum 使用时间桶聚合。'
          : '各图按来源独立标识；ES 查询结果不代表 Oracle 接通或生产适用性已验证。'
      }}统计值仅基于返回采样点，缺失桶与采样间隔缺口不会连成连续曲线。</span
    >
  </div>
  <div v-if="read.loading.value" class="loading-line"></div>
  <EmptyState v-if="read.error.value" :error="read.error.value" @retry="load" />
  <div v-else-if="!dashboard && read.loading.value" class="business-dashboard-grid">
    <div v-for="i in 6" :key="i" class="loading-skeleton"></div>
  </div>
  <EmptyState
    v-else-if="dashboard && !dashboard.charts.length"
    title="业务尚未关联启用指标"
    description="前往指标管理关联并启用指标后查看趋势。"
  />
  <div v-if="dashboard" class="row spread" style="margin-bottom: 13px">
    <span class="small-text muted"
      >关联指标 <strong class="purple">{{ dashboard.charts.length }}</strong> 项</span
    ><span class="range-label"
      >{{ time(dashboard.window.from) }} — {{ time(dashboard.window.to) }}</span
    >
  </div>
  <div v-if="dashboard" class="business-dashboard-grid" data-testid="business-dashboard-grid">
    <section
      v-for="chart in dashboard.charts"
      :key="chart.indicatorId"
      class="panel"
      :data-testid="`dashboard-chart-${chart.indicatorId}`"
    >
      <div
        style="
          padding: 8px 12px 0;
          display: flex;
          justify-content: flex-end;
          align-items: center;
          gap: 6px;
        "
      >
        <span class="badge gray">{{
          chart.sourceId === 'demo' ? '合成演示' : 'ES 只读 · 未经企业实测'
        }}</span
        ><select
          v-model="kinds[chart.indicatorId]"
          :aria-label="`${chart.name}图表类型`"
          style="border: 0; min-height: 23px; font-size: 9px; padding: 2px 18px 2px 4px"
        >
          <option :value="undefined">折线图</option>
          <option value="line">折线图</option>
          <option value="bar">柱状图</option>
          <option value="funnel">漏斗图</option>
        </select>
      </div>
      <div
        v-for="warning in chart.warnings ?? []"
        :key="warning"
        class="notice yellow"
        style="margin: 8px 12px; font-size: 9px"
      >
        {{ warning }}
      </div>
      <ObservationChart
        :title="chart.name"
        :labels="chartLabels(chart)"
        :series="chartSeries(chart, comparison)"
        :kind="kinds[chart.indicatorId] ?? 'line'"
        :unit="chart.unit"
        :error="chart.error"
      />
      <div
        v-if="!chart.points.length"
        class="small-text muted"
        style="text-align: center; padding: 0 15px 12px"
      >
        {{ chart.emptyReason ?? '当前窗口没有采样点' }}
      </div>
      <div class="chart-api-stats">
        <span
          >平均 <b>{{ num(chart.stats.average) }}</b></span
        ><span
          >最大 <b>{{ num(chart.stats.max) }}</b></span
        ><span
          >最小 <b>{{ num(chart.stats.min) }}</b></span
        ><span
          >总和 <b>{{ num(chart.stats.sum) }}</b></span
        >
      </div>
      <div class="row spread" style="padding: 8px 14px 12px; font-size: 8px; color: #baa4cb">
        <span>更新 {{ time(chart.stats.lastUpdated).slice(11, 19) }}</span
        ><span v-if="comparison !== 'none'"
          >同期差 {{ num(chart.stats.comparisonDeltaPercent) }}%</span
        >
      </div>
    </section>
  </div>
</template>
<style scoped>
.business-dashboard-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 17px;
}
@media (max-width: 900px) {
  .business-dashboard-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
.chart-api-stats {
  display: flex;
  justify-content: space-between;
  padding: 11px 13px;
  border-top: 1px solid #f4ebfa;
  color: #bea6cf;
  font-size: 8px;
}
.chart-api-stats b {
  font-weight: 500;
  color: #aa88c1;
  margin-left: 3px;
}
</style>
