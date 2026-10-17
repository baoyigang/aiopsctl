<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import { api } from '../api';
import { app, notify, params, time } from '../context';
import type {
  BusinessSummary,
  EntityResult,
  Indicator,
  IndicatorInput,
  Overview,
  PageResult,
} from '../types';
import { useRequest } from '../composables/useRequest';
import { useMutation } from '../composables/useMutation';
import EmptyState from '../components/EmptyState.vue';
import FormDialog from '../components/FormDialog.vue';
const route = useRoute(),
  read = useRequest(),
  write = useMutation(),
  result = ref<PageResult<Indicator> | null>(null),
  businesses = ref<BusinessSummary[]>([]);
const search = ref(''),
  source = ref(''),
  enabled = ref(''),
  businessId = ref(String(route.query.businessId ?? '')),
  sourceName = ref(''),
  unreferenced = ref(false),
  page = ref(1),
  pageSize = ref(10),
  selected = ref<string[]>([]),
  columnsOpen = ref(false);
const columns = reactive({
  source: true,
  metric: true,
  unit: true,
  business: true,
  visibility: true,
  owner: true,
  updated: true,
  updatedBy: false,
});
const columnLabels = {
  source: '数据源',
  metric: '指标标识',
  unit: '单位',
  business: '关联业务',
  visibility: '可见性',
  owner: '负责人',
  updated: '更新时间',
  updatedBy: '更新人',
};
const totalPages = computed(() =>
  Math.max(1, Math.ceil((result.value?.total ?? 0) / pageSize.value)),
);
const allSelected = computed(
  () =>
    !!result.value?.items.length &&
    result.value.items.every((item) => selected.value.includes(item.id)),
);
const modal = ref<'edit' | 'delete' | null>(null),
  editing = ref<string | null>(null),
  deleting = ref<Indicator | null>(null),
  draftRevision = ref(0);
const draft = reactive<IndicatorInput>({
  spaceId: '',
  name: '',
  sourceId: 'demo',
  sourceName: '合成指标源',
  metricName: '',
  unit: '次',
  owner: '',
  visibility: 'space',
  enabled: true,
  businessIds: [],
  behavior: 'normal',
});
async function load() {
  result.value = null;
  selected.value = [];
  await read.run(
    async (signal) => {
      const [list, businessList] = await Promise.all([
        api<PageResult<Indicator>>(
          '/api/indicators?' +
            params({
              spaceId: app.spaceId,
              search: search.value,
              sourceId: source.value,
              enabled: enabled.value,
              businessId: businessId.value,
              sourceName: sourceName.value,
              unreferenced: unreferenced.value || undefined,
              page: page.value,
              pageSize: pageSize.value,
            }),
          { signal },
        ),
        api<Overview>('/api/overview?' + params({ spaceId: app.spaceId }), { signal }),
      ]);
      return { list, businessList };
    },
    (value) => {
      result.value = value.list;
      businesses.value = value.businessList.items;
    },
  );
}
watch(
  () => app.spaceId,
  (_value, previous) => {
    page.value = 1;
    modal.value = null;
    businessId.value = previous === undefined ? String(route.query.businessId ?? '') : '';
    load();
  },
  { immediate: true },
);
watch(
  () => route.query.businessId,
  (value) => {
    businessId.value = String(value ?? '');
    page.value = 1;
    load();
  },
);
function filter() {
  page.value = 1;
  load();
}
function reset() {
  search.value = '';
  source.value = '';
  enabled.value = '';
  businessId.value = '';
  sourceName.value = '';
  unreferenced.value = false;
  filter();
}
function edit(item?: Indicator) {
  write.reset();
  editing.value = item?.id ?? null;
  draftRevision.value = result.value?.revision ?? app.meta?.revision ?? 0;
  Object.assign(
    draft,
    item
      ? {
          spaceId: item.spaceId,
          name: item.name,
          sourceId: item.sourceId,
          sourceName: item.sourceName,
          metricName: item.metricName,
          unit: item.unit,
          owner: item.owner,
          visibility: item.visibility,
          enabled: item.enabled,
          businessIds: [...item.businessIds],
          behavior: item.behavior,
        }
      : {
          spaceId: app.spaceId,
          name: '',
          sourceId: 'demo',
          sourceName: '合成指标源',
          metricName: '',
          unit: '次',
          owner: '',
          visibility: 'space',
          enabled: true,
          businessIds: [],
          behavior: 'normal',
        },
  );
  modal.value = 'edit';
}
async function save() {
  await write.mutate(
    () =>
      api<EntityResult<Indicator>>(
        editing.value ? `/api/indicators/${editing.value}` : '/api/indicators',
        {
          method: editing.value ? 'PUT' : 'POST',
          body: {
            revision: draftRevision.value,
            data: { ...draft, businessIds: [...draft.businessIds] },
          },
        },
      ),
    async () => {
      modal.value = null;
      notify('指标已保存');
      await load();
    },
  );
}
function askDelete(item: Indicator) {
  write.reset();
  deleting.value = item;
  draftRevision.value = result.value!.revision;
  modal.value = 'delete';
}
async function remove() {
  if (!deleting.value) return;
  await write.mutate(
    () =>
      api(`/api/indicators/${deleting.value!.id}?revision=${draftRevision.value}`, {
        method: 'DELETE',
      }),
    async () => {
      modal.value = null;
      notify('指标已删除');
      await load();
    },
  );
}
async function batch(value: boolean) {
  if (!selected.value.length || !result.value) return;
  await write.mutate(
    () =>
      api<{ revision: number; updatedCount: number }>('/api/indicators/batch', {
        method: 'POST',
        body: {
          revision: result.value!.revision,
          ids: [...selected.value],
          spaceId: app.spaceId,
          enabled: value,
        },
      }),
    async (result) => {
      notify(`已${value ? '启用' : '停用'} ${result.updatedCount} 个指标`);
      await load();
    },
  );
}
function selectAll(checked: boolean) {
  selected.value = checked ? (result.value?.items.map((item) => item.id) ?? []) : [];
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
      <h1>指标管理</h1>
      <p>统一管理指标定义、业务关联与可见范围，筛选条件按 AND 组合。</p>
    </div>
    <div class="page-actions">
      <button class="button secondary" @click="load">↻ 刷新</button
      ><button class="button primary" data-testid="indicator-create" @click="edit()">
        ＋ 新建指标
      </button>
    </div>
  </div>
  <div class="notice">
    <span>◌</span
    ><span>当前为合成演示。创建“ES”指标定义不会建立真实连接；数据源未就绪时图表明确显示错误。</span>
  </div>
  <div class="tabs">
    <button
      :class="{ active: !unreferenced }"
      @click="
        unreferenced = false;
        filter();
      "
    >
      全部指标</button
    ><button
      :class="{ active: unreferenced }"
      data-testid="indicator-unreferenced"
      @click="
        unreferenced = true;
        filter();
      "
    >
      未关联业务
    </button>
  </div>
  <section class="panel">
    <div class="panel-body">
      <form class="filters" @submit.prevent="filter">
        <input
          v-model="search"
          data-testid="indicator-search"
          placeholder="搜索名称、标识、负责人"
          aria-label="搜索指标"
        /><select v-model="source" data-testid="indicator-source" aria-label="数据源">
          <option value="">全部数据源</option>
          <option value="demo">合成指标源</option>
          <option value="es-main">Elasticsearch（未验证）</option></select
        ><select v-model="enabled" data-testid="indicator-enabled" aria-label="启用状态">
          <option value="">全部状态</option>
          <option value="true">启用</option>
          <option value="false">停用</option></select
        ><select v-model="businessId" data-testid="indicator-business" aria-label="关联业务">
          <option value="">全部业务</option>
          <option v-for="business in businesses" :key="business.id" :value="business.id">
            {{ business.name }}
          </option></select
        ><input
          v-model="sourceName"
          data-testid="indicator-source-name"
          style="width: 150px"
          placeholder="数据源名称"
          aria-label="数据源名称"
        /><button class="button primary">查询</button
        ><button type="button" class="button text" @click="reset">重置</button>
      </form>
    </div>
    <div style="padding: 0 20px 15px" class="row spread wrap">
      <div class="row">
        <small>已选 {{ selected.length }} 项</small
        ><button
          class="button secondary small"
          data-testid="indicator-batch-enable"
          :disabled="!selected.length || write.saving.value"
          @click="batch(true)"
        >
          批量启用</button
        ><button
          class="button secondary small"
          data-testid="indicator-batch-disable"
          :disabled="!selected.length || write.saving.value"
          @click="batch(false)"
        >
          批量停用
        </button>
      </div>
      <div class="columns-container">
        <button
          class="button secondary small"
          data-testid="indicator-columns"
          :aria-expanded="columnsOpen"
          @click="columnsOpen = !columnsOpen"
        >
          ▥ 列显示
        </button>
        <div v-if="columnsOpen" class="columns-menu">
          <label v-for="(label, key) in columnLabels" :key="key" class="checkbox-label"
            ><input
              v-model="columns[key]"
              type="checkbox"
              :data-testid="`indicator-column-${key}`"
            />{{ label }}</label
          >
        </div>
      </div>
    </div>
    <div
      v-if="write.error.value && !modal"
      class="notice error"
      role="alert"
      style="margin: 0 20px 15px"
    >
      {{ write.error.value
      }}<button
        @click="
          load();
          write.reset();
        "
      >
        刷新列表
      </button>
    </div>
    <div v-if="read.loading.value" class="loading-line"></div>
    <EmptyState v-if="read.error.value" :error="read.error.value" @retry="load" /><EmptyState
      v-else-if="!read.loading.value && !result?.items.length"
      title="没有匹配的指标"
      description="检查组合筛选条件，或创建一个新指标。"
    />
    <div v-else-if="result?.items.length" class="table-wrap">
      <table class="data-table">
        <thead>
          <tr>
            <th class="check">
              <input
                type="checkbox"
                aria-label="选择当前页全部指标"
                :checked="allSelected"
                @change="selectAll(($event.target as HTMLInputElement).checked)"
              />
            </th>
            <th>指标名称</th>
            <th v-if="columns.source">数据源</th>
            <th v-if="columns.metric">指标标识</th>
            <th v-if="columns.unit">单位</th>
            <th v-if="columns.business">关联业务</th>
            <th>状态</th>
            <th v-if="columns.visibility">可见性</th>
            <th v-if="columns.owner">负责人</th>
            <th v-if="columns.updated">更新时间</th>
            <th v-if="columns.updatedBy">更新人</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="item in result.items" :key="item.id">
            <td class="check">
              <input
                v-model="selected"
                :value="item.id"
                type="checkbox"
                :data-testid="`indicator-select-${item.id}`"
                :aria-label="`选择${item.name}`"
              />
            </td>
            <td>
              <div class="primary-cell">{{ item.name }}</div>
              <div class="sub-cell">{{ item.id }}</div>
            </td>
            <td v-if="columns.source">
              <span class="badge" :class="item.sourceId === 'demo' ? 'gray' : 'yellow'">{{
                item.sourceName
              }}</span>
            </td>
            <td v-if="columns.metric" class="mono">{{ item.metricName }}</td>
            <td v-if="columns.unit">{{ item.unit }}</td>
            <td v-if="columns.business">
              <div v-if="!item.businessIds.length" class="muted">未关联</div>
              <div v-for="id in item.businessIds" :key="id" class="sub-cell">
                {{ businesses.find((business) => business.id === id)?.name ?? id }}
              </div>
            </td>
            <td>
              <span class="badge" :class="item.enabled ? 'green' : 'gray'">{{
                item.enabled ? '启用' : '停用'
              }}</span>
            </td>
            <td v-if="columns.visibility">
              {{ item.visibility === 'space' ? '空间内' : '演示公共' }}
            </td>
            <td v-if="columns.owner">{{ item.owner }}</td>
            <td v-if="columns.updated" style="font-size: 9px; white-space: nowrap">
              {{ time(item.updatedAt) }}
            </td>
            <td v-if="columns.updatedBy">{{ item.updatedBy }}</td>
            <td>
              <div class="row" style="gap: 3px">
                <button
                  class="button text small"
                  :data-testid="`indicator-edit-${item.id}`"
                  @click="edit(item)"
                >
                  编辑</button
                ><button
                  class="button text small"
                  :data-testid="`indicator-delete-${item.id}`"
                  @click="askDelete(item)"
                >
                  删除
                </button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <div class="table-footer">
      <span>共 {{ result?.total ?? 0 }} 项 · 当前页选择仅作用于本页</span>
      <div class="pagination">
        <select
          v-model.number="pageSize"
          aria-label="每页数量"
          data-testid="indicator-page-size"
          style="min-height: 27px; font-size: 10px; padding: 3px 22px 3px 8px"
          @change="filter"
        >
          <option :value="5">5 条/页</option>
          <option :value="10">10 条/页</option>
          <option :value="20">20 条/页</option></select
        ><button :disabled="page <= 1 || read.loading.value" @click="changePage(page - 1)">‹</button
        ><span data-testid="indicator-page">{{ page }} / {{ totalPages }}</span
        ><button
          data-testid="indicator-page-next"
          :disabled="page >= totalPages || read.loading.value"
          @click="changePage(page + 1)"
        >
          ›
        </button>
      </div>
    </div>
  </section>
  <FormDialog
    v-if="modal"
    :title="modal === 'delete' ? '删除指标' : editing ? '编辑指标' : '新建指标'"
    :busy="write.saving.value"
    @close="modal = null"
    ><div v-if="write.error.value" class="notice error" role="alert">
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
          >指标名称<input
            v-model="draft.name"
            data-testid="indicator-name"
            required
            maxlength="100" /></label
        ><label
          >负责人<input
            v-model="draft.owner"
            data-testid="indicator-owner"
            required
            maxlength="80" /></label
        ><label
          >数据源<select v-model="draft.sourceId" data-testid="indicator-form-source">
            <option value="demo">合成源 demo</option>
            <option value="es-main">Elasticsearch（未验证）</option>
          </select></label
        ><label
          >数据源名称<input
            v-model="draft.sourceName"
            data-testid="indicator-form-source-name"
            required
            maxlength="100" /></label
        ><label
          >指标标识<input
            v-model="draft.metricName"
            data-testid="indicator-metric"
            required
            maxlength="120"
            placeholder="如 service.requests" /></label
        ><label
          >单位<input
            v-model="draft.unit"
            data-testid="indicator-unit"
            required
            maxlength="30" /></label
        ><label
          >可见性<select v-model="draft.visibility" data-testid="indicator-visibility">
            <option value="space">当前空间</option>
            <option value="public">演示公共（不对外发布）</option>
          </select></label
        ><label
          >演示采样行为<select v-model="draft.behavior" data-testid="indicator-behavior">
            <option value="normal">正常合成采样</option>
            <option value="empty">空数据，用于验证空态</option>
          </select></label
        >
      </div>
      <label class="checkbox-label" style="margin-top: 17px"
        ><input
          v-model="draft.enabled"
          data-testid="indicator-form-enabled"
          type="checkbox"
        />启用指标</label
      ><label style="margin-top: 17px">关联业务</label>
      <div class="form-grid" style="margin-top: 8px; gap: 10px">
        <label v-for="business in businesses" :key="business.id" class="checkbox-label"
          ><input
            v-model="draft.businessIds"
            :value="business.id"
            :data-testid="`indicator-form-business-${business.id}`"
            type="checkbox"
          />{{ business.name }}</label
        >
      </div>
      <p class="form-help">关联范围限制在当前空间。指标定义不包含任意查询 DSL 或真实数据源凭据。</p>
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
          保存指标
        </button>
      </div>
    </form>
    <div v-else>
      <p class="small-text muted" style="line-height: 1.9">
        确定删除「{{ deleting?.name }}」？关联业务或被大屏使用的指标不能删除，需先明确解除引用。
      </p>
      <div class="dialog-actions">
        <button class="button secondary" @click="modal = null">取消</button
        ><button
          class="button danger"
          data-testid="indicator-delete-confirm"
          :disabled="write.saving.value"
          @click="remove"
        >
          确认删除
        </button>
      </div>
    </div></FormDialog
  >
</template>
