<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue';
import { api } from '../api';
import { chartLabels, chartSeries, sourceLabel } from '../chartData';
import { app, notify, params, time } from '../context';
import type {
  BusinessDetail,
  BusinessSummary,
  EntityResult,
  Indicator,
  Overview,
  PageResult,
  Screen,
  ScreenExport,
  ScreenInput,
  ScreenPreview,
} from '../types';
import { useRequest } from '../composables/useRequest';
import { useMutation } from '../composables/useMutation';
import EmptyState from '../components/EmptyState.vue';
import FormDialog from '../components/FormDialog.vue';
import ObservationChart from '../components/charts/ObservationChart.vue';
const read = useRequest(),
  panelRead = useRequest(),
  previewRead = useRequest(),
  write = useMutation();
const result = ref<PageResult<Screen> | null>(null),
  businesses = ref<BusinessSummary[]>([]),
  available = ref<Indicator[]>([]),
  search = ref(''),
  page = ref(1),
  pageSize = ref(9);
const modal = ref<'edit' | 'delete' | 'import' | 'preview' | null>(null),
  editing = ref<string | null>(null),
  deleting = ref<Screen | null>(null),
  preview = ref<ScreenPreview | null>(null),
  draftRevision = ref(0),
  importContent = ref(''),
  importInput = ref<HTMLInputElement | null>(null),
  exporting = ref('');
const draft = reactive<ScreenInput>({
  spaceId: '',
  name: '',
  businessId: '',
  columns: 2,
  panels: [],
});
const currentSpaceName = computed(
  () => app.meta?.spaces.find((space) => space.id === app.spaceId)?.name ?? app.spaceId,
);
const totalPages = computed(() =>
  Math.max(1, Math.ceil((result.value?.total ?? 0) / pageSize.value)),
);
async function load() {
  result.value = null;
  await read.run(
    async (signal) => {
      const [screens, overview] = await Promise.all([
        api<PageResult<Screen>>(
          '/api/screens?' +
            params({
              spaceId: app.spaceId,
              search: search.value,
              page: page.value,
              pageSize: pageSize.value,
            }),
          { signal },
        ),
        api<Overview>('/api/overview?' + params({ spaceId: app.spaceId }), { signal }),
      ]);
      return { screens, overview };
    },
    (value) => {
      result.value = value.screens;
      businesses.value = value.overview.items;
    },
  );
}
watch(
  () => app.spaceId,
  () => {
    modal.value = null;
    preview.value = null;
    page.value = 1;
    load();
  },
  { immediate: true },
);
async function loadIndicators(defaultPanels = false) {
  available.value = [];
  if (!draft.businessId) return;
  await panelRead.run(
    (signal) =>
      api<BusinessDetail>(`/api/businesses/${encodeURIComponent(draft.businessId)}`, { signal }),
    (value) => {
      available.value = value.indicators;
      if (defaultPanels)
        draft.panels = value.indicators
          .slice(0, 4)
          .map((item) => ({ indicatorId: item.id, kind: 'line', aggregation: 'avg' }));
    },
  );
}
function create() {
  write.reset();
  editing.value = null;
  draftRevision.value = result.value?.revision ?? app.meta?.revision ?? 0;
  Object.assign(draft, {
    spaceId: app.spaceId,
    name: '',
    businessId: businesses.value[0]?.id ?? '',
    columns: 2,
    panels: [],
  });
  modal.value = 'edit';
  loadIndicators(true);
}
async function edit(item: Screen) {
  write.reset();
  editing.value = item.id;
  modal.value = 'edit';
  draftRevision.value = result.value!.revision;
  Object.assign(draft, {
    spaceId: item.spaceId,
    name: item.name,
    businessId: item.businessId,
    columns: item.columns,
    panels: item.panels.map((panel) => ({ ...panel })),
  });
  await loadIndicators();
}
function changeBusiness() {
  draft.panels = [];
  loadIndicators(true);
}
function addPanel() {
  const first = available.value[0];
  if (first && draft.panels.length < 24)
    draft.panels.push({ indicatorId: first.id, kind: 'line', aggregation: 'avg' });
}
async function save() {
  await write.mutate(
    async () => {
      if (!draft.panels.length) throw new Error('至少添加一个图表面板。');
      return await api<EntityResult<Screen>>(
        editing.value ? `/api/screens/${editing.value}` : '/api/screens',
        {
          method: editing.value ? 'PUT' : 'POST',
          body: {
            revision: draftRevision.value,
            data: { ...draft, panels: draft.panels.map((panel) => ({ ...panel })) },
          },
        },
      );
    },
    async () => {
      modal.value = null;
      notify('大屏已保存');
      await load();
    },
  );
}
function askDelete(item: Screen) {
  write.reset();
  deleting.value = item;
  draftRevision.value = result.value!.revision;
  modal.value = 'delete';
}
async function remove() {
  if (!deleting.value) return;
  await write.mutate(
    () =>
      api(`/api/screens/${deleting.value!.id}?revision=${draftRevision.value}`, {
        method: 'DELETE',
      }),
    async () => {
      modal.value = null;
      notify('大屏已删除');
      await load();
    },
  );
}
function openImport() {
  write.reset();
  importContent.value = '';
  draftRevision.value = result.value?.revision ?? app.meta?.revision ?? 0;
  modal.value = 'import';
}
async function chooseFile(event: Event) {
  write.reset();
  const file = (event.target as HTMLInputElement).files?.[0];
  if (!file) return;
  try {
    if (!/\.json$/i.test(file.name)) throw new Error('仅支持 .json 大屏配置文件。');
    if (file.size > 128 * 1024) throw new Error('文件不能超过 128 KiB。');
    importContent.value = new TextDecoder('utf-8', { fatal: true }).decode(
      await file.arrayBuffer(),
    );
  } catch (value) {
    write.error.value =
      value instanceof TypeError
        ? '文件不是有效的 UTF-8 文本。'
        : value instanceof Error
          ? value.message
          : '读取文件失败。';
    if (importInput.value) importInput.value.value = '';
  }
}
async function importScreen() {
  await write.mutate(
    async () => {
      if (new TextEncoder().encode(importContent.value).byteLength > 128 * 1024)
        throw new Error('配置不能超过 128 KiB。');
      let document: unknown;
      try {
        document = JSON.parse(importContent.value);
      } catch {
        throw new Error('JSON 格式无效，请检查后重试。');
      }
      return await api<EntityResult<Screen>>('/api/screens/import', {
        method: 'POST',
        body: { revision: draftRevision.value, spaceId: app.spaceId, document },
      });
    },
    async () => {
      modal.value = null;
      notify('大屏已导入，服务端已校验空间与指标引用');
      await load();
    },
  );
}
async function exportScreen(item: Screen) {
  if (exporting.value) return;
  exporting.value = item.id;
  try {
    const exported = await api<ScreenExport>(`/api/screens/${item.id}/export`);
    const blob = new Blob([JSON.stringify(exported, null, 2)], {
      type: 'application/json;charset=utf-8',
    });
    const url = URL.createObjectURL(blob),
      link = document.createElement('a');
    link.href = url;
    link.download = item.name.replace(/[\\/:*?"<>|]/g, '_') + '.json';
    link.click();
    URL.revokeObjectURL(url);
    notify('已导出大屏配置（不含凭据、ID 和更新时间）');
  } catch (value) {
    app.error = value instanceof Error ? value.message : '导出失败';
  } finally {
    exporting.value = '';
  }
}
async function openPreview(item: Screen) {
  preview.value = null;
  modal.value = 'preview';
  await previewRead.run(
    (signal) => api<ScreenPreview>(`/api/screens/${item.id}/preview`, { signal }),
    (value) => (preview.value = value),
  );
}
function close() {
  if (write.saving.value) return;
  modal.value = null;
  panelRead.cancel();
  previewRead.cancel();
  preview.value = null;
}
function panelChart(index: number) {
  return (
    preview.value?.dashboard.charts[index] ??
    preview.value?.dashboard.charts.find(
      (chart) => chart.indicatorId === preview.value?.screen.panels[index]?.indicatorId,
    )
  );
}
function changePage(next: number) {
  if (next < 1 || next > totalPages.value) return;
  page.value = next;
  load();
}
</script>
<template>
  <div class="page-header">
    <div>
      <h1>自定义大屏</h1>
      <p>将关键指标组合成一张业务看板，用适合的视角呈现趋势。</p>
    </div>
    <div class="page-actions">
      <button class="button secondary" data-testid="screen-import" @click="openImport">
        ↥ 导入配置</button
      ><button class="button primary" data-testid="screen-create" @click="create">
        ＋ 新建大屏
      </button>
    </div>
  </div>
  <div class="notice">
    <span>◌</span
    ><span
      >合成演示 · 大屏配置只引用当前空间内已存在的业务与指标。导入不会执行脚本、HTML 或
      URL，不创建数据源连接。</span
    >
  </div>
  <div class="toolbar">
    <form
      class="filters"
      @submit.prevent="
        page = 1;
        load();
      "
    >
      <input
        v-model="search"
        data-testid="screen-search"
        placeholder="搜索大屏名称"
        aria-label="搜索大屏"
      /><button class="button secondary">查询</button
      ><button
        type="button"
        class="button text"
        @click="
          search = '';
          page = 1;
          load();
        "
      >
        重置
      </button>
    </form>
    <span class="range-label">共 {{ result?.total ?? 0 }} 张大屏</span>
  </div>
  <div v-if="read.loading.value" class="loading-line"></div>
  <EmptyState v-if="read.error.value" :error="read.error.value" @retry="load" /><EmptyState
    v-else-if="!read.loading.value && !result?.items.length"
    title="当前空间暂无大屏"
    description="新建一张业务大屏，或导入经过校验的 JSON 配置。"
  />
  <div class="grid-three">
    <article
      v-for="screen in result?.items"
      :key="screen.id"
      class="business-card"
      :data-testid="`screen-card-${screen.id}`"
    >
      <div class="screen-thumbnail" aria-hidden="true">
        <div
          class="screen-mini-grid"
          :style="{ gridTemplateColumns: `repeat(${screen.columns},1fr)` }"
        >
          <div
            v-for="(panel, index) in screen.panels.slice(0, 6)"
            :key="index"
            class="screen-mini-tile"
          >
            <small
              >{{ panel.kind === 'bar' ? '柱状' : panel.kind === 'funnel' ? '漏斗' : '趋势' }} ·
              {{ panel.aggregation }}</small
            >
            <div class="screen-mini-chart">
              <i
                v-for="i in 11"
                :key="i"
                :style="{
                  height: `${25 + ((i * 13 + index * 19) % 65)}%`,
                  background: index % 3 === 1 ? '#e9d58f' : index % 3 === 2 ? '#b0dfce' : '#dac1ef',
                }"
              ></i>
            </div>
          </div>
        </div>
      </div>
      <div class="screen-card-body">
        <h3>{{ screen.name }}</h3>
        <p>
          {{ businesses.find((item) => item.id === screen.businessId)?.name ?? screen.businessId }}
          · {{ screen.panels.length }} 个面板 · {{ screen.columns }} 列布局
        </p>
        <div class="screen-card-meta">{{ time(screen.updatedAt) }} · {{ screen.updatedBy }}</div>
        <small style="font-size: 8px; display: block; margin-top: 7px"
          >示意缩略图 · 非实际采样</small
        >
      </div>
      <div class="screen-card-actions">
        <button
          class="button text small"
          :data-testid="`screen-preview-${screen.id}`"
          @click="openPreview(screen)"
        >
          预览</button
        ><button
          class="button text small"
          :data-testid="`screen-edit-${screen.id}`"
          @click="edit(screen)"
        >
          编辑</button
        ><button
          class="button text small"
          :data-testid="`screen-export-${screen.id}`"
          :disabled="!!exporting"
          @click="exportScreen(screen)"
        >
          导出</button
        ><button
          class="button text small"
          :data-testid="`screen-delete-${screen.id}`"
          @click="askDelete(screen)"
        >
          删除
        </button>
      </div>
    </article>
  </div>
  <div v-if="result?.total" class="table-footer" style="border: 0; padding: 18px 0">
    <span>第 {{ page }} / {{ totalPages }} 页</span>
    <div class="pagination">
      <button :disabled="page <= 1" @click="changePage(page - 1)">上一页</button
      ><button :disabled="page >= totalPages" @click="changePage(page + 1)">下一页</button>
    </div>
  </div>
  <FormDialog
    v-if="modal"
    :title="
      modal === 'edit'
        ? editing
          ? '编辑大屏'
          : '新建大屏'
        : modal === 'delete'
          ? '删除大屏'
          : modal === 'import'
            ? '导入大屏配置'
            : (preview?.screen.name ?? '大屏预览')
    "
    :wide="modal === 'edit' || modal === 'preview'"
    :busy="write.saving.value"
    @close="close"
    ><div v-if="write.error.value && modal !== 'preview'" class="notice error" role="alert">
      {{ write.error.value
      }}<button
        v-if="write.conflict.value"
        @click="write.refreshRevision((value) => (draftRevision = value))"
      >
        刷新版本并保留草稿
      </button>
    </div>
    <form v-if="modal === 'edit'" @submit.prevent="save">
      <div class="form-grid">
        <label
          >大屏名称<input
            v-model="draft.name"
            data-testid="screen-name"
            required
            maxlength="100" /></label
        ><label
          >布局列数<select v-model.number="draft.columns" data-testid="screen-columns">
            <option :value="1">1 列</option>
            <option :value="2">2 列</option>
            <option :value="3">3 列</option>
          </select></label
        ><label style="grid-column: 1/-1"
          >关联业务<select
            v-model="draft.businessId"
            data-testid="screen-business"
            required
            @change="changeBusiness"
          >
            <option v-for="business in businesses" :key="business.id" :value="business.id">
              {{ business.name }}
            </option>
          </select></label
        >
      </div>
      <div class="row spread" style="margin-top: 23px; margin-bottom: 13px">
        <h3 style="font-size: 12px; color: #b092c2">
          图表面板 <small> {{ draft.panels.length }} / 24</small>
        </h3>
        <button
          type="button"
          class="button secondary small"
          data-testid="screen-add-panel"
          :disabled="panelRead.loading.value || !available.length || draft.panels.length >= 24"
          @click="addPanel"
        >
          ＋ 添加面板
        </button>
      </div>
      <div v-if="panelRead.loading.value" class="loading-line"></div>
      <div v-if="panelRead.error.value" class="notice error">{{ panelRead.error.value }}</div>
      <div v-if="!panelRead.loading.value && !available.length" class="notice yellow">
        所选业务没有可用关联指标，请先在指标管理中关联。
      </div>
      <div v-for="(panel, index) in draft.panels" :key="index" class="screen-panel-edit">
        <span class="muted" style="font-size: 10px">{{ String(index + 1).padStart(2, '0') }}</span
        ><select
          v-model="panel.indicatorId"
          :data-testid="`screen-panel-indicator-${index}`"
          aria-label="面板指标"
          required
          style="flex: 1; min-width: 120px"
        >
          <option v-for="indicator in available" :key="indicator.id" :value="indicator.id">
            {{ indicator.name }}
          </option></select
        ><select
          v-model="panel.kind"
          :data-testid="`screen-panel-kind-${index}`"
          aria-label="图表类型"
        >
          <option value="line">折线图</option>
          <option value="bar">柱状图</option>
          <option value="funnel">漏斗图</option></select
        ><select
          v-model="panel.aggregation"
          :data-testid="`screen-panel-aggregation-${index}`"
          aria-label="聚合方式"
        >
          <option value="avg">平均 avg</option>
          <option value="sum">求和 sum</option>
          <option value="raw">原始 raw</option></select
        ><button
          type="button"
          class="icon-button"
          aria-label="移除面板"
          @click="draft.panels.splice(index, 1)"
        >
          ×
        </button>
      </div>
      <p class="form-help">
        面板顺序即预览顺序。每个面板独立选择聚合方式，漏斗按同一采样系列展示，不代表真实转化链路。
      </p>
      <div class="dialog-actions">
        <button
          type="button"
          class="button secondary"
          data-testid="form-cancel"
          :disabled="write.saving.value"
          @click="close"
        >
          取消</button
        ><button
          class="button primary"
          data-testid="form-save"
          :disabled="write.saving.value || panelRead.loading.value || !draft.panels.length"
        >
          保存大屏
        </button>
      </div>
    </form>
    <form v-else-if="modal === 'import'" @submit.prevent="importScreen">
      <label
        >配置文件（UTF-8 JSON，最大 128 KiB）<input
          ref="importInput"
          data-testid="screen-import-file"
          type="file"
          accept=".json,application/json"
          @change="chooseFile" /></label
      ><label style="margin-top: 16px"
        >JSON 内容<textarea
          v-model="importContent"
          data-testid="screen-import-content"
          class="mono"
          rows="12"
          required
          placeholder='{"format":"hengchuan-screen","version":1,"screen":{…}}'
        />
      </label>
      <p class="form-help">
        目标空间：{{
          currentSpaceName
        }}。仅支持本站导出格式；重复名称拒绝覆盖，导入失败不会改变已有数据。
      </p>
      <div class="dialog-actions">
        <button type="button" class="button secondary" @click="close">取消</button
        ><button
          class="button primary"
          data-testid="screen-import-submit"
          :disabled="write.saving.value"
        >
          校验并导入
        </button>
      </div>
    </form>
    <div v-else-if="modal === 'delete'">
      <p class="small-text muted">确定删除大屏「{{ deleting?.name }}」？业务与原指标将保留。</p>
      <div class="dialog-actions">
        <button class="button secondary" @click="close">取消</button
        ><button
          class="button danger"
          data-testid="screen-delete-confirm"
          :disabled="write.saving.value"
          @click="remove"
        >
          确认删除
        </button>
      </div>
    </div>
    <div v-else>
      <div v-if="previewRead.loading.value" class="loading-skeleton"></div>
      <EmptyState v-else-if="previewRead.error.value" :error="previewRead.error.value" /><template
        v-else-if="preview"
        ><div class="notice">
          {{ sourceLabel(preview.dashboard.mode) }}预览 · {{ preview.dashboard.window.label }} · UTC
          · 各面板独立聚合
        </div>
        <div
          class="preview-grid"
          :style="{ gridTemplateColumns: `repeat(${preview.screen.columns},minmax(0,1fr))` }"
        >
          <div v-for="(panel, index) in preview.screen.panels" :key="index" class="panel">
            <div
              v-if="panelChart(index)"
              class="small-text muted"
              style="padding: 9px 12px 0; font-size: 9px"
            >
              {{
                panelChart(index)!.sourceId === 'demo' ? '合成演示' : 'ES 只读结果 · 未经企业实测'
              }}
            </div>
            <div
              v-for="warning in panelChart(index)?.warnings ?? []"
              :key="warning"
              class="notice yellow"
              style="margin: 8px 12px; font-size: 9px"
            >
              {{ warning }}
            </div>
            <ObservationChart
              v-if="panelChart(index)"
              :title="panelChart(index)!.name"
              :labels="chartLabels(panelChart(index)!)"
              :series="chartSeries(panelChart(index)!)"
              :kind="panel.kind"
              :unit="panelChart(index)!.unit"
              :error="panelChart(index)!.error"
            /><EmptyState v-else title="面板引用没有对应图表" />
            <div
              class="small-text muted"
              style="padding: 8px 13px; border-top: 1px solid #f3e8fb; font-size: 9px"
            >
              {{ panel.aggregation }} · {{ panel.kind }} · {{ panel.indicatorId }}
            </div>
          </div>
        </div></template
      >
    </div></FormDialog
  >
</template>
<style scoped>
.screen-panel-edit {
  display: flex;
  gap: 9px;
  align-items: center;
  padding: 11px 0;
  border-bottom: 1px solid #f1e7f9;
}
.screen-panel-edit select {
  font-size: 10px;
}
.screen-panel-edit .icon-button {
  flex-shrink: 0;
}
@media (max-width: 650px) {
  .screen-panel-edit {
    flex-wrap: wrap;
  }
  .preview-grid {
    grid-template-columns: 1fr !important;
  }
}
</style>
