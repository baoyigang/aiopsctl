<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue';
import { RouterLink, useRouter } from 'vue-router';
import { api } from '../api';
import { app, loadMeta, notify, num, params, spaceRoute, time } from '../context';
import type {
  BusinessInput,
  BusinessSummary,
  Dashboard,
  EntityResult,
  Overview,
  Space,
} from '../types';
import { useRequest } from '../composables/useRequest';
import { useMutation } from '../composables/useMutation';
import { useAutoRefresh } from '../composables/useAutoRefresh';
import EmptyState from '../components/EmptyState.vue';
import FormDialog from '../components/FormDialog.vue';
import MicroTrend from '../components/charts/MicroTrend.vue';
const router = useRouter();
const result = ref<Overview | null>(null),
  search = ref(''),
  trends = ref<Record<string, number[]>>({});
const read = useRequest(),
  write = useMutation();
const modal = ref<'business' | 'delete' | 'space' | null>(null),
  editing = ref<string | null>(null),
  draftRevision = ref(0),
  spaceName = ref('');
const draft = reactive<BusinessInput>({
  spaceId: '',
  name: '',
  owner: '',
  description: '',
  service: '',
  environment: 'demo',
  targetAvailability: 99.9,
});
const deleting = ref<BusinessSummary | null>(null);
function preference(key: string, fallback: string): string {
  try {
    return localStorage.getItem(key) ?? fallback;
  } catch {
    return fallback;
  }
}
function savePreference(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    /* Storage is optional. */
  }
}
const layout = ref<'cards' | 'compact'>(
  preference('hengchuan.overview.layout', 'cards') === 'compact' ? 'compact' : 'cards',
);
const savedInterval = Number(preference('hengchuan.overview.refreshSeconds', '0'));
const refreshSeconds = ref([0, 30, 60].includes(savedInterval) ? savedInterval : 0);
const lastRefreshed = ref<string | null>(null);
const autoRefresh = useAutoRefresh({
  intervalSeconds: refreshSeconds,
  isBusy: () => read.loading.value || !!modal.value || write.saving.value,
  refresh: () => fetchOverview(false),
});
watch(layout, (value) => savePreference('hengchuan.overview.layout', value));
watch(refreshSeconds, (value) =>
  savePreference('hengchuan.overview.refreshSeconds', String(value)),
);
const average = computed(() => {
  const valid = result.value?.items.filter((item) => item.availability != null) ?? [];
  return valid.length
    ? valid.reduce((total, item) => total + (item.availability ?? 0), 0) / valid.length
    : null;
});
const events = computed(
  () => result.value?.items.reduce((total, item) => total + item.eventCount, 0) ?? 0,
);
async function load() {
  autoRefresh.reset();
  await fetchOverview(true);
}
async function fetchOverview(clearResults: boolean) {
  if (clearResults) {
    result.value = null;
    trends.value = {};
    lastRefreshed.value = null;
  }
  await read.run(
    async (signal) => {
      const overview = await api<Overview>(
        '/api/overview?' + params({ spaceId: app.spaceId, search: search.value }),
        { signal },
      );
      const charts = await Promise.all(
        overview.items.map(async (item) => {
          try {
            const dashboard = await api<Dashboard>(
              `/api/businesses/${encodeURIComponent(item.id)}/dashboard`,
              { signal },
            );
            return [
              item.id,
              dashboard.charts
                .find((chart) => chart.sourceId === 'demo' && chart.points.length)
                ?.points.map((point) => point.value)
                .filter((value): value is number => value != null) ?? [],
            ] as const;
          } catch {
            return [item.id, []] as const;
          }
        }),
      );
      return { overview, charts };
    },
    (value) => {
      result.value = value.overview;
      trends.value = Object.fromEntries(value.charts);
      lastRefreshed.value = new Date().toISOString();
    },
  );
}
watch(
  () => app.spaceId,
  () => {
    modal.value = null;
    load();
  },
  { immediate: true },
);
function openBusiness(item?: BusinessSummary) {
  write.reset();
  editing.value = item?.id ?? null;
  draftRevision.value = result.value?.revision ?? app.meta?.revision ?? 0;
  Object.assign(
    draft,
    item
      ? {
          spaceId: item.spaceId,
          name: item.name,
          owner: item.owner,
          description: item.description,
          service: item.service,
          environment: item.environment,
          targetAvailability: item.targetAvailability,
        }
      : {
          spaceId: app.spaceId,
          name: '',
          owner: '',
          description: '',
          service: '',
          environment: 'demo',
          targetAvailability: 99.9,
        },
  );
  modal.value = 'business';
}
async function save() {
  await write.mutate(
    () =>
      api<EntityResult<BusinessInput>>(
        editing.value ? `/api/businesses/${editing.value}` : '/api/businesses',
        {
          method: editing.value ? 'PUT' : 'POST',
          body: { revision: draftRevision.value, data: { ...draft } },
        },
      ),
    async () => {
      modal.value = null;
      notify(editing.value ? '业务已更新' : '业务已创建');
      await load();
      await loadMeta();
    },
  );
}
function askDelete(item: BusinessSummary) {
  write.reset();
  deleting.value = item;
  draftRevision.value = result.value!.revision;
  modal.value = 'delete';
}
async function remove() {
  if (!deleting.value) return;
  await write.mutate(
    () =>
      api(`/api/businesses/${deleting.value!.id}?revision=${draftRevision.value}`, {
        method: 'DELETE',
      }),
    async () => {
      modal.value = null;
      notify('业务已删除');
      await load();
      await loadMeta();
    },
  );
}
function openSpace() {
  write.reset();
  spaceName.value = '';
  draftRevision.value = app.meta?.revision ?? result.value?.revision ?? 0;
  modal.value = 'space';
}
async function saveSpace() {
  await write.mutate(
    () =>
      api<EntityResult<Space>>('/api/spaces', {
        method: 'POST',
        body: { revision: draftRevision.value, data: { name: spaceName.value } },
      }),
    async (value) => {
      modal.value = null;
      await loadMeta();
      await router.push(spaceRoute('/polaris', value.data.id));
      notify('工作空间已创建');
    },
  );
}
</script>
<template>
  <div class="page-header">
    <div>
      <h1>业务概览</h1>
      <p>从服务目标到每一次波动，持续关注业务的可用性与稳定性。</p>
    </div>
    <div class="page-actions">
      <select
        v-model.number="refreshSeconds"
        data-testid="business-auto-refresh"
        aria-label="自动刷新间隔"
      >
        <option :value="0">自动刷新：关闭</option>
        <option :value="30">自动刷新：30 秒</option>
        <option :value="60">自动刷新：60 秒</option>
      </select>
      <button
        class="button secondary"
        data-testid="business-refresh"
        :disabled="read.loading.value"
        @click="load"
      >
        ↻ 刷新</button
      ><button class="button secondary" data-testid="space-create" @click="openSpace">
        新建空间</button
      ><button class="button primary" data-testid="business-create" @click="openBusiness()">
        ＋ 新建业务
      </button>
    </div>
  </div>
  <div class="notice">
    <span>◌</span
    ><span
      >业务与事件目录为合成演示 · 首页统计最近 30 天，卡片趋势仅取最近 12 小时合成源采样；ES
      结果见业务详情。所有时间为 UTC，Oracle 未接入。</span
    >
  </div>
  <div class="stat-strip">
    <div class="stat-card">
      <div class="label">◈ 观测业务</div>
      <div class="value">{{ result?.items.length ?? '—' }}<small>个</small></div>
      <div class="foot">当前空间 · 当前筛选范围</div>
    </div>
    <div class="stat-card">
      <div class="label">◴ 平均可用性</div>
      <div class="value">{{ num(average, 3) }}<small>%</small></div>
      <div class="foot">业务可用性算术平均 · 最近 30 天</div>
    </div>
    <div class="stat-card">
      <div class="label">▥ 关联指标</div>
      <div class="value">
        {{ result?.items.reduce((sum, item) => sum + item.chartCount, 0) ?? '—' }}<small>项</small>
      </div>
      <div class="foot">关联指标数量，非独立指标去重数</div>
    </div>
    <div class="stat-card">
      <div class="label">⌁ 发生事件</div>
      <div class="value">{{ result ? events : '—' }}<small>次</small></div>
      <div class="foot">与统计窗口相交的事件</div>
    </div>
  </div>
  <div class="toolbar">
    <form class="filters" @submit.prevent="load">
      <input
        v-model="search"
        data-testid="business-search"
        placeholder="搜索业务名称、负责人或服务"
        aria-label="搜索业务"
      /><button class="button secondary">查询</button
      ><button
        v-if="search"
        type="button"
        class="button text"
        @click="
          search = '';
          load();
        "
      >
        重置
      </button>
    </form>
    <div class="row wrap">
      <span v-if="refreshSeconds" class="range-label" data-testid="business-refresh-countdown">{{
        read.loading.value
          ? '刷新中…'
          : autoRefresh.paused.value
            ? '自动刷新已暂停'
            : `${autoRefresh.remaining.value} 秒后刷新`
      }}</span>
      <div class="segmented" role="group" aria-label="业务布局">
        <button
          type="button"
          data-testid="business-layout-cards"
          :class="{ active: layout === 'cards' }"
          :aria-pressed="layout === 'cards'"
          @click="layout = 'cards'"
        >
          ▦ 双列卡片
        </button>
        <button
          type="button"
          data-testid="business-layout-compact"
          :class="{ active: layout === 'compact' }"
          :aria-pressed="layout === 'compact'"
          @click="layout = 'compact'"
        >
          ☷ 紧凑列表
        </button>
      </div>
    </div>
  </div>
  <div class="row spread" style="margin: -5px 0 14px">
    <span class="range-label">{{ result?.window.label ?? '最近 30 天' }} · UTC</span
    ><span v-if="lastRefreshed" class="range-label"
      >上次刷新 {{ time(lastRefreshed).slice(11, 19) }} UTC</span
    >
  </div>
  <div v-if="read.loading.value" class="loading-line"></div>
  <EmptyState v-if="read.error.value" :error="read.error.value" @retry="load" />
  <EmptyState
    v-else-if="!read.loading.value && !result?.items.length"
    title="当前空间暂无匹配业务"
    description="新建一个业务，或调整搜索词后重试。"
  />
  <div
    v-if="layout === 'compact' && result?.items.length"
    class="panel"
    data-testid="business-layout-content"
    data-layout="compact"
  >
    <div class="table-wrap">
      <table class="data-table">
        <thead>
          <tr>
            <th>业务 / 服务</th>
            <th>负责人</th>
            <th>可用性 / 目标</th>
            <th>错误预算消耗</th>
            <th>剩余预算</th>
            <th>指标 / 事件</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="item in result.items" :key="item.id" :data-testid="`business-card-${item.id}`">
            <td>
              <RouterLink class="primary-cell" :to="spaceRoute(`/polaris/n/${encodeURIComponent(item.id)}`)">{{
                item.name
              }}</RouterLink>
              <div class="sub-cell">{{ item.service }} · {{ item.environment }}</div>
            </td>
            <td>{{ item.owner }}</td>
            <td>
              <span
                class="badge"
                :class="
                  item.availability != null && item.availability >= item.targetAvailability
                    ? 'green'
                    : 'yellow'
                "
                >{{ num(item.availability, 3) }}%</span
              >
              <div class="sub-cell">目标 {{ num(item.targetAvailability, 3) }}%</div>
            </td>
            <td>
              {{ num(item.consumedPercent) }}%
              <div class="sub-cell">
                {{ num(item.consumedMinutes, 1) }} / {{ num(item.quotaMinutes, 1) }} 分钟
              </div>
            </td>
            <td>{{ num(item.remainingMinutes, 1) }} 分钟</td>
            <td>{{ item.chartCount }} 项 / {{ item.eventCount }} 次</td>
            <td>
              <div class="row">
                <button
                  class="button text small"
                  :data-testid="`business-edit-${item.id}`"
                  @click="openBusiness(item)"
                >
                  编辑</button
                ><button
                  class="button text small"
                  :data-testid="`business-delete-${item.id}`"
                  @click="askDelete(item)"
                >
                  删除</button
                ><RouterLink
                  :to="spaceRoute(`/polaris/n/${encodeURIComponent(item.id)}`)"
                  :data-testid="`business-drill-${item.id}`"
                  >进入 →</RouterLink
                >
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
  <div v-else class="grid-two" data-testid="business-layout-content" data-layout="cards">
    <article
      v-for="(item, index) in result?.items"
      :key="item.id"
      class="business-card"
      :data-testid="`business-card-${item.id}`"
    >
      <div class="business-card-head">
        <span class="business-icon">{{ ['◈', '▧', '⌘', '◷'][index % 4] }}</span>
        <div>
          <RouterLink :to="spaceRoute(`/polaris/n/${encodeURIComponent(item.id)}`)"
            ><h2>{{ item.name }}</h2></RouterLink
          ><small>{{ item.service }} · {{ item.environment }} · 负责人 {{ item.owner }}</small>
        </div>
        <div class="spacer"></div>
        <span
          class="badge"
          :class="(item.availability ?? 0) >= item.targetAvailability ? 'green' : 'yellow'"
          ><i
            class="status-dot"
            :class="{ warn: (item.availability ?? 0) < item.targetAvailability }"
          ></i
          >{{
            item.availability == null
              ? '无观测窗口'
              : item.availability >= item.targetAvailability
                ? '达到目标'
                : '低于目标'
          }}</span
        ><button
          class="icon-button"
          :data-testid="`business-edit-${item.id}`"
          aria-label="编辑业务"
          @click="openBusiness(item)"
        >
          ⋯
        </button>
      </div>
      <div class="business-card-body">
        <div class="row spread">
          <span class="small-text muted">可用性 <small> / 最近 30 天</small></span
          ><span class="small-text muted">目标 {{ num(item.targetAvailability, 3) }}%</span>
        </div>
        <div class="row spread" style="margin-top: 12px">
          <strong style="font-size: 28px; font-weight: 500; color: #7c6495"
            >{{ num(item.availability, 3)
            }}<small style="display: inline; font-size: 13px; margin-left: 4px">%</small></strong
          >
          <div style="width: 47%; height: 45px">
            <MicroTrend
              :values="trends[item.id] ?? []"
              :color="index % 2 ? '#65cbb7' : '#9b78da'"
              :height="45"
            />
          </div>
        </div>
        <div
          style="
            height: 5px;
            background: #f1eaf8;
            border-radius: 4px;
            overflow: hidden;
            margin-top: 18px;
          "
        >
          <div
            :style="{
              height: '100%',
              width: `${Math.max(0, Math.min(100, item.consumedPercent))}%`,
              background: item.consumedPercent > 100 ? '#e8a1ad' : '#b99ae3',
            }"
          ></div>
        </div>
        <div class="row spread" style="margin-top: 8px">
          <small>错误预算消耗 {{ num(item.consumedPercent) }}%</small
          ><small>{{ item.eventCount }} 次事件</small>
        </div>
        <div class="business-metrics" style="margin-top: 23px">
          <div class="mini-metric">
            <div class="label">错误预算</div>
            <div class="value">{{ num(item.quotaMinutes, 1) }}<small>分钟</small></div>
          </div>
          <div class="mini-metric">
            <div class="label">已消耗</div>
            <div class="value">{{ num(item.consumedMinutes, 1) }}<small>分钟</small></div>
          </div>
          <div class="mini-metric">
            <div class="label">剩余预算</div>
            <div class="value">{{ num(item.remainingMinutes, 1) }}<small>分钟</small></div>
          </div>
        </div>
      </div>
      <div class="business-card-footer">
        <span>{{ item.chartCount }} 个关联指标 · 更新 {{ time(item.updatedAt).slice(0, 16) }}</span>
        <div class="row">
          <button
            class="button text small"
            :data-testid="`business-delete-${item.id}`"
            @click="askDelete(item)"
          >
            删除</button
          ><RouterLink
            :to="spaceRoute(`/polaris/n/${encodeURIComponent(item.id)}`)"
            :data-testid="`business-drill-${item.id}`"
            >进入业务 →</RouterLink
          >
        </div>
      </div>
    </article>
  </div>
  <FormDialog
    v-if="modal"
    :title="
      modal === 'business'
        ? editing
          ? '编辑业务'
          : '新建业务'
        : modal === 'space'
          ? '新建工作空间'
          : '删除业务'
    "
    :busy="write.saving.value"
    @close="modal = null"
    ><div v-if="write.error.value" class="notice error" role="alert">
      {{ write.error.value
      }}<button
        v-if="write.conflict.value"
        type="button"
        @click="write.refreshRevision((value) => (draftRevision = value))"
      >
        刷新版本并保留草稿
      </button>
    </div>
    <form v-if="modal === 'business'" @submit.prevent="save">
      <div class="form-grid">
        <label
          >业务名称<span class="required">*</span
          ><input v-model="draft.name" data-testid="business-name" required maxlength="80" /></label
        ><label
          >负责人<span class="required">*</span
          ><input
            v-model="draft.owner"
            data-testid="business-owner"
            required
            maxlength="80" /></label
        ><label
          >服务标识<span class="required">*</span
          ><input
            v-model="draft.service"
            data-testid="business-service"
            required
            maxlength="100" /></label
        ><label
          >环境<span class="required">*</span
          ><input
            v-model="draft.environment"
            data-testid="business-environment"
            required
            maxlength="60" /></label
        ><label
          >目标可用性（%）<input
            v-model.number="draft.targetAvailability"
            data-testid="business-target"
            type="number"
            min="90"
            max="99.999"
            step=".001"
            required /></label
        ><label
          >工作空间<select v-model="draft.spaceId" disabled>
            <option v-for="space in app.meta?.spaces" :key="space.id" :value="space.id">
              {{ space.name }}
            </option>
          </select></label
        >
      </div>
      <label style="margin-top: 17px"
        >业务描述<textarea
          v-model="draft.description"
          data-testid="business-description"
          rows="3"
          maxlength="1000"
        />
      </label>
      <div class="dialog-actions">
        <button
          type="button"
          class="button secondary"
          data-testid="form-cancel"
          :disabled="write.saving.value"
          @click="modal = null"
        >
          取消</button
        ><button class="button primary" data-testid="form-save" :disabled="write.saving.value">
          {{ write.saving.value ? '保存中…' : '保存业务' }}
        </button>
      </div>
    </form>
    <form v-else-if="modal === 'space'" @submit.prevent="saveSpace">
      <label
        >空间名称<input v-model="spaceName" data-testid="space-name" maxlength="80" required
      /></label>
      <div class="dialog-actions">
        <button type="button" class="button secondary" @click="modal = null">取消</button
        ><button class="button primary" data-testid="form-save" :disabled="write.saving.value">
          创建空间
        </button>
      </div>
    </form>
    <div v-else>
      <p class="small-text muted" style="line-height: 1.9">
        确定删除「{{
          deleting?.name
        }}」？已被指标、事件或大屏引用的业务无法删除，服务端会拒绝操作并保留数据。
      </p>
      <div class="dialog-actions">
        <button class="button secondary" @click="modal = null">取消</button
        ><button
          class="button danger"
          data-testid="business-delete-confirm"
          :disabled="write.saving.value"
          @click="remove"
        >
          确认删除
        </button>
      </div>
    </div></FormDialog
  >
</template>
