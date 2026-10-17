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

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/polaris' },
    { path: '/polaris', component: OverviewPage },
    { path: '/polaris/n/:business', component: BusinessPage },
    { path: '/polaris/indicator', component: IndicatorPage },
    { path: '/polaris/statuspage', component: StatusPage },
    { path: '/polaris/stability', component: StabilityPage },
    { path: '/polaris/screen', component: ScreenPage },
    { path: '/:pathMatch(.*)*', redirect: '/polaris' },
  ],
  scrollBehavior: () => ({ top: 0 }),
});
createApp(App).use(router).mount('#app');
