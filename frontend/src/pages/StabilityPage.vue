<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { RouterLink } from 'vue-router';
import { api } from '../api';
import { app, num, params, time } from '../context';
import type { EntityResult, IncidentEvent, Stability } from '../types';
import { useRequest } from '../composables/useRequest';
import EmptyState from '../components/EmptyState.vue';
const read = useRequest(),
  eventRead = useRequest(),
  result = ref<Stability | null>(null),
  period = ref('year'),
  year = ref(new Date().getUTCFullYear()),
  anchor = ref(new Date().toISOString().slice(0, 10)),
  level = ref('all'),
  expanded = ref<string | null>(null),
  eventDetail = ref<IncidentEvent | null>(null);
const periodNames: Record<string, string> = {
  year: '自然年',
  month: '自然月',
  week: 'ISO 周',
  day: '自然日',
};
const businessNames = computed(() =>
  Object.fromEntries(result.value?.items.map((item) => [item.id, item.name]) ?? []),
);
async function load() {
  result.value = null;
  expanded.value = null;
  eventDetail.value = null;
  eventRead.cancel();
  await read.run(
    (signal) =>
      api<Stability>(
        '/api/stability?' +
          params({
            spaceId: app.spaceId,
            year: year.value,
            level: level.value,
            period: period.value,
            anchor: anchor.value,
          }),
        { signal },
      ),
    (value) => (result.value = value),
  );
}
watch(() => app.spaceId, load, { immediate: true });
function changeYear() {
  anchor.value = String(year.value) + anchor.value.slice(4);
  load();
}
async function openEvent(id: string) {
  if (expanded.value === id) {
    expanded.value = null;
    eventDetail.value = null;
    eventRead.cancel();
    return;
  }
  expanded.value = id;
  eventDetail.value = null;
  await eventRead.run(
    (signal) => api<EntityResult<IncidentEvent>>(`/api/events/${id}`, { signal }),
    (value) => (eventDetail.value = value.data),
  );
}
function duration(event: IncidentEvent) {
  const end = new Date(event.endedAt ?? result.value?.window.to ?? Date.now()).getTime(),
    start = new Date(event.startedAt).getTime();
  return Math.max(0, (end - start) / 60000);
}
</script>
<template>
  <div class="page-header">
    <div>
      <h1>稳定性统计</h1>
      <p>在清楚的统计口径下，评估服务目标、错误预算与恢复表现。</p>
    </div>
    <div class="page-actions">
      <button class="button secondary" @click="load">↻ 刷新统计</button>
    </div>
  </div>
  <div class="panel" style="padding: 16px 18px; margin-bottom: 18px">
    <div class="filters">
      <span class="filter-label">统计周期</span
      ><select v-model="period" data-testid="stability-period" @change="load">
        <option value="year">自然年</option>
        <option value="month">自然月</option>
        <option value="week">ISO 周</option>
        <option value="day">自然日</option></select
      ><label class="row"
        >年份<input
          v-model.number="year"
          data-testid="stability-year"
          type="number"
          min="2020"
          max="2100"
          style="width: 95px; margin: 0"
          @change="changeYear" /></label
      ><label class="row"
        >锚定日期<input
          v-model="anchor"
          data-testid="stability-anchor"
          type="date"
          style="width: 145px; margin: 0"
          @change="load" /></label
      ><span class="filter-separator"></span><span class="filter-label">事件级别</span
      ><select v-model="level" data-testid="stability-level" @change="load">
        <option value="all">全部级别</option>
        <option value="P1">P1 严重</option>
        <option value="P2">P2 重要</option>
        <option value="P3">P3 一般</option></select
      ><span class="spacer"></span><span class="badge">{{ periodNames[period] }} · UTC</span>
    </div>
  </div>
  <div class="notice">
    <span>◌</span
    ><span
      >合成演示 ·
      {{
        result?.window.label ?? '所选周期与年份取交集，且截至当前时间'
      }}。事件级别筛选会影响全部事件派生统计。首页最近 30 天与本页{{
        periodNames[period]
      }}口径不同。</span
    >
  </div>
  <div class="stat-strip">
    <div class="stat-card">
      <div class="label">观测业务</div>
      <div class="value">{{ result?.summary.businessCount ?? '—' }}<small>个</small></div>
      <div class="foot">低于目标 {{ result?.summary.belowTargetCount ?? '—' }} 个</div>
    </div>
    <div class="stat-card">
      <div class="label">窗口内事件</div>
      <div class="value">{{ result?.summary.eventCount ?? '—' }}<small>次</small></div>
      <div class="foot">{{ level === 'all' ? '全部级别' : level }} · 相交原始事件数</div>
    </div>
    <div class="stat-card">
      <div class="label">累计停机时间</div>
      <div class="value">{{ num(result?.summary.consumedMinutes, 1) }}<small>分钟</small></div>
      <div class="foot">每业务事件区间并集后汇总</div>
    </div>
    <div class="stat-card">
      <div class="label">错误预算总额</div>
      <div class="value">{{ num(result?.summary.quotaMinutes, 1) }}<small>分钟</small></div>
      <div class="foot">有效观测时长 × (1 - 目标可用性)</div>
    </div>
  </div>
  <div v-if="read.loading.value" class="loading-line"></div>
  <EmptyState v-if="read.error.value" :error="read.error.value" @retry="load" /><template
    v-else-if="result"
    ><div class="row spread" style="margin-bottom: 14px">
      <h2>业务稳定性</h2>
      <span class="range-label">{{ time(result.window.from) }} — {{ time(result.window.to) }}</span>
    </div>
    <section class="panel">
      <EmptyState
        v-if="!result.items.length"
        title="所选周期暂无观测数据"
        description="未来窗口不会返回虚构的 100% 可用性。请选择有效日期。"
      />
      <div v-else class="table-wrap">
        <table class="data-table">
          <thead>
            <tr>
              <th>业务</th>
              <th>可用性 / 目标</th>
              <th>错误预算</th>
              <th>预算消耗</th>
              <th>剩余预算</th>
              <th>事件数</th>
              <th>MTTR</th>
              <th>MTBF</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in result.items" :key="item.id">
              <td>
                <RouterLink :to="`/polaris/n/${item.id}`" class="primary-cell">{{
                  item.name
                }}</RouterLink>
                <div class="sub-cell">{{ item.owner }}</div>
              </td>
              <td>
                <span
                  :style="{
                    color:
                      item.availability != null && item.availability < item.targetAvailability
                        ? '#d29a7a'
                        : '#89bda9',
                  }"
                  >{{ num(item.availability, 3) }}%</span
                >
                <div class="sub-cell">目标 {{ num(item.targetAvailability, 3) }}%</div>
              </td>
              <td>{{ num(item.quotaMinutes, 1) }} 分钟</td>
              <td>
                <div>
                  {{ num(item.consumedMinutes, 1) }} 分钟
                  <small> / {{ num(item.consumedPercent) }}%</small>
                </div>
                <div
                  style="
                    height: 4px;
                    border-radius: 2px;
                    overflow: hidden;
                    background: #f1e8f7;
                    margin-top: 9px;
                    width: 100px;
                  "
                >
                  <div
                    :style="{
                      height: '100%',
                      width: `${Math.max(0, Math.min(100, item.consumedPercent))}%`,
                      background: item.consumedPercent > 100 ? '#e9adb5' : '#bf9fe4',
                    }"
                  ></div>
                </div>
              </td>
              <td>{{ num(item.remainingMinutes, 1) }} 分钟</td>
              <td>{{ item.eventCount }}</td>
              <td>
                {{ num(item.mttrMinutes, 1) }}<small v-if="item.mttrMinutes != null"> 分钟</small>
              </td>
              <td>
                {{ num(item.mtbfMinutes, 1) }}<small v-if="item.mtbfMinutes != null"> 分钟</small>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
    <p class="form-help" style="margin-top: 12px">
      MTTR：已恢复事件在窗口内截断时长的平均；MTBF：(观测时长 - 停机时长) /
      事件数。无可用样本展示“—”，不补造统计值。
    </p>
    <section class="panel section-gap">
      <div class="panel-header">
        <h2>
          事件记录
          <span class="badge gray" style="margin-left: 7px">{{ result.events.length }}</span>
        </h2>
        <span class="range-label">点击展开原始事件详情</span>
      </div>
      <EmptyState
        v-if="!result.events.length"
        title="此窗口内无匹配事件"
        description="这表示没有匹配记录，不能据此断言真实生产系统健康。"
      />
      <div v-else class="table-wrap">
        <table class="data-table">
          <thead>
            <tr>
              <th>事件 / 业务</th>
              <th>级别</th>
              <th>开始时间</th>
              <th>持续时长（原事件）</th>
              <th>状态</th>
              <th>详情</th>
            </tr>
          </thead>
          <tbody>
            <template v-for="event in result.events" :key="event.id"
              ><tr>
                <td>
                  <div class="primary-cell">{{ event.title }}</div>
                  <div class="sub-cell">
                    {{ businessNames[event.businessId] ?? event.businessId }}
                  </div>
                </td>
                <td>
                  <span
                    class="badge"
                    :class="event.level === 'P1' ? 'red' : event.level === 'P2' ? 'yellow' : 'gray'"
                    >{{ event.level }}</span
                  >
                </td>
                <td style="font-size: 10px">{{ time(event.startedAt) }}</td>
                <td>{{ num(duration(event), 1) }} 分钟</td>
                <td>
                  <span class="badge" :class="event.endedAt ? 'green' : 'red'">{{
                    event.endedAt ? '已恢复' : '进行中'
                  }}</span>
                </td>
                <td>
                  <button
                    class="button text small"
                    :data-testid="`event-expand-${event.id}`"
                    :aria-expanded="expanded === event.id"
                    @click="openEvent(event.id)"
                  >
                    {{ expanded === event.id ? '收起 ⌃' : '展开 ⌄' }}
                  </button>
                </td>
              </tr>
              <tr v-if="expanded === event.id">
                <td colspan="6" :data-testid="`event-details-${event.id}`">
                  <div v-if="eventRead.loading.value" class="muted small-text">
                    正在加载事件原记录…
                  </div>
                  <div v-else-if="eventRead.error.value" class="notice error">
                    {{ eventRead.error.value }}
                  </div>
                  <div v-else-if="eventDetail" class="event-detail">
                    <strong>{{ eventDetail.title }}</strong>
                    <p style="white-space: pre-wrap">{{ eventDetail.description }}</p>
                    <div>
                      开始：{{ time(eventDetail.startedAt) }}　恢复：{{
                        eventDetail.endedAt ? time(eventDetail.endedAt) : '尚未恢复'
                      }}
                    </div>
                    <div>来源：隔离合成事件 · {{ eventDetail.id }}</div>
                    <small>事件详情显示原始区间；上方统计按窗口边界截断并去除重叠。</small>
                  </div>
                </td>
              </tr></template
            >
          </tbody>
        </table>
      </div>
    </section></template
  >
</template>
