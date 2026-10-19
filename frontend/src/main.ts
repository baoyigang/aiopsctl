import { createApp } from 'vue';
import { createRouter, createWebHistory } from 'vue-router';
import App from './App.vue';
import OverviewPage from './pages/OverviewPage.vue';
import BusinessPage from './pages/BusinessPage.vue';
import IndicatorPage from './pages/IndicatorPage.vue';
import StatusPage from './pages/StatusPage.vue';
import StabilityPage from './pages/StabilityPage.vue';
import ScreenPage from './pages/ScreenPage.vue';
import './style.css';
import { app, loadMeta } from './context';
import { resolveSpaceQuery, spaceQuery } from './spaceQuery';
import { api } from './api';
import type { BusinessDetail } from './types';

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: (to) => ({ path: '/polaris', query: to.query, hash: to.hash }) },
    { path: '/polaris', component: OverviewPage },
    { path: '/polaris/n/:business', component: BusinessPage },
    { path: '/polaris/indicator', component: IndicatorPage },
    { path: '/polaris/statuspage', component: StatusPage },
    { path: '/polaris/stability', component: StabilityPage },
    { path: '/polaris/screen', component: ScreenPage },
    { path: '/:pathMatch(.*)*', redirect: (to) => ({ path: '/polaris', query: to.query, hash: to.hash }) },
  ],
  scrollBehavior: () => ({ top: 0 }),
});
let spaceNavigation = 0;
router.beforeEach(async (to) => {
  const navigation = ++spaceNavigation;
  try {
    if (!app.meta) await loadMeta();
  } catch (error) {
    if (navigation !== spaceNavigation) return false;
    app.error = error instanceof Error ? error.message : '无法加载工作空间。';
    return;
  }
  if (navigation !== spaceNavigation) return false;
  let preferredId = app.spaceId;
  if (to.params.business && !('space' in to.query) && !('spaceId' in to.query)) {
    // Old unscoped detail bookmarks can infer their space; explicit URL scope is authoritative.
    try {
      const business = await api<BusinessDetail>(
        `/api/businesses/${encodeURIComponent(String(to.params.business))}`,
      );
      preferredId = business.data.spaceId;
    } catch { /* The detail page retains its normal missing-business error. */ }
  }
  if (navigation !== spaceNavigation) return false;
  const result = resolveSpaceQuery(app.meta!.spaces, to.query, preferredId);
  if (result.space) {
    const query = spaceQuery(to.query, result.space, app.meta!.spaces);
    if (to.query.space !== query.space || to.query.spaceId !== query.spaceId)
      return { path: to.path, query, hash: to.hash, replace: true };
  }
});
router.afterEach((to, _from, failure) => {
  if (failure || !app.meta) return;
  const result = resolveSpaceQuery(app.meta.spaces, to.query, app.spaceId);
  app.spaceError = result.error;
  app.spaceId = result.space?.id ?? '';
  app.spaceReady = true;
});
createApp(App).use(router).mount('#app');
