<script setup lang="ts">
import { computed, onMounted } from 'vue';
import { app, loadMeta } from './context';
import { useRoute } from 'vue-router';
const route = useRoute();
onMounted(() => loadMeta().catch((error) => (app.error = error.message)));
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
      <RouterLink class="brand" to="/polaris"
        ><span class="brand-symbol" aria-hidden="true"><i></i><i></i><i></i></span>
        <div>衡川观测<small>HENGCHUAN OBSERVE</small></div></RouterLink
      >
      <div class="sidebar-section">业务可观测</div>
      <nav aria-label="主导航">
        <RouterLink
          v-for="link in links"
          :key="link.path"
          :to="link.path"
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
            v-model="app.spaceId"
            data-testid="space-select"
            aria-label="工作空间"
            style="border: 0; color: #9a91a6; font-size: 11px"
          >
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
        <RouterView />
      </main>
      <footer class="app-footer">
        <span>衡川观测 · 原创业务可观测工作台</span><span>本地开发工作台 · 未提供生产身份认证</span>
      </footer>
    </div>
    <div v-if="app.toast" class="toast" role="status">{{ app.toast }}</div>
  </div>
</template>
