<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { RouterLink } from 'vue-router';
import { api } from '../api';
import { num } from '../context';
import type { StatusMatrix } from '../types';
import { useRequest } from '../composables/useRequest';
import EmptyState from '../components/EmptyState.vue';
const read = useRequest(),
  result = ref<StatusMatrix | null>(null),
  expanded = ref<string[]>([]);
async function load() {
  result.value = null;
  await read.run(
    (signal) => api<StatusMatrix>('/api/statuspage', { signal }),
    (value) => {
      result.value = value;
      if (!expanded.value.length && value.spaces[0]) expanded.value = [value.spaces[0].id];
    },
  );
}
function toggle(id: string) {
  expanded.value = expanded.value.includes(id)
    ? expanded.value.filter((value) => value !== id)
    : [...expanded.value, id];
}
onMounted(load);
</script>
<template>
  <div class="page-header">
    <div>
      <h1>服务状态页</h1>
      <p>跨空间查看业务运行状态，用最近 7 个自然日串起每一次波动。</p>
    </div>
    <div class="page-actions">
      <button
        class="button secondary"
        data-testid="status-expand-all"
        @click="expanded = result?.spaces.map((space) => space.id) ?? []"
      >
        展开全部</button
      ><button class="button secondary" @click="load">↻ 刷新</button>
    </div>
  </div>
  <div class="notice">
    <span>◌</span
    ><span
      >合成演示 · 此页展示全部演示空间，不受顶部空间选择影响。{{
        result?.windowLabel ?? '最近 7 个 UTC 自然日（含今日）'
      }}，今日计算截至当前时间。</span
    >
  </div>
  <section class="panel">
    <div class="panel-header">
      <div>
        <h2>业务健康矩阵</h2>
        <p>点击空间展开业务；事件重叠时停机时长不重复累计。</p>
      </div>
      <div class="status-legend">
        <span><i></i>正常</span><span><i class="bad"></i>存在事件</span
        ><span><i style="background: #e7dfed"></i>无观测</span>
      </div>
    </div>
    <div v-if="read.loading.value" class="loading-line"></div>
    <EmptyState v-if="read.error.value" :error="read.error.value" @retry="load" /><EmptyState
      v-else-if="!read.loading.value && !result?.spaces.length"
      title="暂无空间或业务"
    />
    <div v-else-if="result" class="table-wrap">
      <table class="data-table status-matrix">
        <thead>
          <tr>
            <th>空间 / 业务</th>
            <th v-for="day in result.days" :key="day">
              {{ day.slice(5) }}
              <div class="sub-cell" style="margin-top: 4px">{{ day }}</div>
            </th>
            <th>最新状态</th>
          </tr>
        </thead>
        <tbody>
          <template v-for="space in result.spaces" :key="space.id"
            ><tr style="background: #fdfbff">
              <td :colspan="result.days.length + 2">
                <div class="matrix-row-label">
                  <button
                    class="matrix-expand"
                    :data-testid="`status-space-toggle-${space.id}`"
                    :aria-expanded="expanded.includes(space.id)"
                    :aria-label="`展开${space.name}`"
                    @click="toggle(space.id)"
                  >
                    {{ expanded.includes(space.id) ? '⌄' : '›' }}</button
                  ><strong style="font-weight: 500; color: #a181b7">{{ space.name }}</strong
                  ><span class="badge gray">{{ space.businesses.length }} 个业务</span>
                </div>
              </td>
            </tr>
            <template v-if="expanded.includes(space.id)"
              ><tr
                v-for="business in space.businesses"
                :key="business.id"
                class="matrix-subrow"
                :data-testid="`status-business-${business.id}`"
              >
                <td>
                  <RouterLink :to="`/polaris/n/${business.id}`">{{ business.name }}</RouterLink>
                </td>
                <td v-for="day in business.days" :key="day.date">
                  <div class="matrix-cell" :title="`${day.date} · ${day.eventCount} 次事件`">
                    <span
                      class="matrix-bar"
                      :class="{ bad: day.status === 'incident' }"
                      :style="day.status === 'no-data' ? { background: '#e7dfed' } : {}"
                    ></span
                    ><span style="font-size: 10px">{{
                      day.status === 'no-data' ? '—' : `${num(day.availability, 3)}%`
                    }}</span
                    ><small style="font-size: 8px">{{
                      day.eventCount
                        ? `${day.eventCount} 次事件`
                        : day.status === 'no-data'
                          ? '无观测窗口'
                          : '无事件'
                    }}</small>
                  </div>
                </td>
                <td>
                  <span class="badge" :class="business.latestStatus === 'healthy' ? 'green' : 'red'"
                    ><i class="status-dot" :class="{ bad: business.latestStatus !== 'healthy' }"></i
                    >{{ business.latestStatus === 'healthy' ? '正常运行' : '事件进行中' }}</span
                  >
                </td>
              </tr>
              <tr v-if="!space.businesses.length">
                <td :colspan="result.days.length + 2"><span class="muted">空间内暂无业务</span></td>
              </tr></template
            ></template
          >
        </tbody>
      </table>
    </div>
  </section>
</template>
