<script setup lang="ts">
import { computed } from 'vue';
import { app, spaceRoute } from './context';
import { useRoute, useRouter } from 'vue-router';
import { spaceQuery } from './spaceQuery';
const route = useRoute(), router = useRouter();
function selectSpace(event: Event) {
  const space = app.meta?.spaces.find((item) => item.id === (event.target as HTMLSelectElement).value);
  if (!space) return;
  const query = spaceQuery(route.query, space, app.meta?.spaces);
  if (space.id !== app.spaceId) delete query.businessId;
  router.push({
    path: route.path.startsWith('/polaris/n/') ? '/polaris' : route.path,
    query,
    hash: route.hash,
  });
}
const links = [
  { path: '/polaris', label: '业务概览', icon: '◈' },
  { path: '/polaris/indicator', label: '指标管理', icon: '▥' },
  { path: '/polaris/statuspage', label: '状态页', icon: '▦' },
  { path: '/polaris/stability', label: '稳定性统计', icon: '◴' },
  { path: '/polaris/screen', label: '自定义大屏', icon: '▣' },
];
const current = computed(() =>
  route.path.startsWith('/polaris/n/')
    ? '业务详情'
    : (links.find((item) => item.path === route.path)?.label ?? '业务概览'),
);
function selected(path: string) {
  return path === '/polaris'
    ? route.path === path || route.path.startsWith('/polaris/n/')
    : route.path === path;
}
</script>

<template>
  <div class="app-layout">
    <aside class="sidebar">
      <RouterLink class="brand" :to="spaceRoute('/polaris')"
        ><span class="brand-symbol" aria-hidden="true"><i></i><i></i><i></i></span>
        <div>衡川观测<small>HENGCHUAN OBSERVE</small></div></RouterLink
      >
      <div class="sidebar-section">业务可观测</div>
      <nav aria-label="主导航">
        <RouterLink
          v-for="link in links"
          :key="link.path"
          :to="spaceRoute(link.path)"
          :class="{ selected: selected(link.path) }"
          :data-testid="`nav-${link.path.split('/').pop()}`"
          ><span>{{ link.icon }}</span
          >{{ link.label }}</RouterLink
        >
      </nav>
      <div class="sidebar-bottom">
        <div class="sidebar-avatar">衡</div>
        <div>演示工作区<small>隔离合成数据</small></div>
        <span class="sidebar-more">···</span>
      </div>
    </aside>
    <div class="app-main">
      <header class="topbar">
        <div class="breadcrumb">
          业务可观测 <span>/</span> <strong>{{ current }}</strong>
        </div>
        <div class="topbar-right">
          <span class="demo-label"><i></i> 演示模式</span><span class="topbar-divider"></span
          ><select
            :value="app.spaceId"
            :disabled="!app.meta || !app.spaceReady"
            @change="selectSpace"
            data-testid="space-select"
            aria-label="工作空间"
            style="border: 0; color: #9a91a6; font-size: 11px"
          >
            <option v-if="!app.spaceId" value="" disabled>请选择空间</option>
            <option v-for="space in app.meta?.spaces" :key="space.id" :value="space.id">
              {{ space.name }}
            </option></select
          ><span class="user-avatar">HC</span>
        </div>
      </header>
      <main class="page">
        <div v-if="app.error" class="notice error" role="alert">
          {{ app.error }}<button @click="app.error = ''">×</button>
        </div>
        <div v-if="app.spaceError" class="notice error" role="alert" data-testid="space-error">
          {{ app.spaceError }} 请使用顶部选择器选择有效空间。
        </div>
        <div v-else-if="!app.spaceReady" class="notice" role="status">
          {{ app.error ? '无法加载工作空间，请刷新重试。' : '正在加载工作空间…' }}
        </div>
        <RouterView v-else :key="app.spaceId" />
      </main>
      <footer class="app-footer">
        <span>衡川观测 · 原创业务可观测工作台</span><span>本地开发工作台 · 未提供生产身份认证</span>
      </footer>
    </div>
    <div v-if="app.toast" class="toast" role="status">{{ app.toast }}</div>
  </div>
</template>
