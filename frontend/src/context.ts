import { reactive } from 'vue';
import { api } from './api';
import type { Meta } from './types';
import type { LocationQuery } from 'vue-router';
import { spaceQuery } from './spaceQuery';
export const app = reactive({
  meta: null as Meta | null,
  spaceId: 'space-default',
  spaceError: '',
  spaceReady: false,
  error: '',
  toast: '',
});
let toastTimer = 0;
let metaRequest: Promise<void> | null = null;
export async function loadMeta() {
  if (metaRequest) return metaRequest;
  metaRequest = api<Meta>('/api/meta')
    .then((meta) => {
      app.meta = meta;
      app.error = '';
    })
    .finally(() => { metaRequest = null; });
  return metaRequest;
}
export function spaceRoute(path: string, spaceId = app.spaceId, query: LocationQuery = {}) {
  const space = app.meta?.spaces.find((item) => item.id === spaceId);
  return { path, query: space ? spaceQuery(query, space, app.meta?.spaces) : query };
}
export function notify(message: string) {
  app.toast = message;
  window.clearTimeout(toastTimer);
  toastTimer = window.setTimeout(() => (app.toast = ''), 4000);
}
export function num(value: number | null | undefined, digits = 2): string {
  return value == null || !Number.isFinite(value)
    ? '—'
    : new Intl.NumberFormat('zh-CN', { maximumFractionDigits: digits }).format(value);
}
export function time(value: string | null | undefined): string {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isFinite(date.getTime())
    ? date.toISOString().slice(0, 19).replace('T', ' ') + ' UTC'
    : value;
}
export function params(values: Record<string, string | number | boolean | undefined>): string {
  const result = new URLSearchParams();
  Object.entries(values).forEach(([key, value]) => {
    if (value !== undefined && value !== '') result.set(key, String(value));
  });
  return result.toString();
}
export function utcInput(iso: string): string {
  return iso.slice(0, 16);
}
export function inputIso(value: string): string {
  const result = new Date(value + 'Z');
  if (!Number.isFinite(result.getTime())) throw new Error('请选择有效的UTC时间。');
  return result.toISOString();
}
