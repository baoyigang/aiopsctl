import { reactive } from 'vue';
import { api } from './api';
import type { Meta } from './types';
export const app = reactive({
  meta: null as Meta | null,
  spaceId: 'space-default',
  error: '',
  toast: '',
});
let toastTimer = 0;
export async function loadMeta() {
  app.meta = await api<Meta>('/api/meta');
  if (!app.meta.spaces.some((item) => item.id === app.spaceId))
    app.spaceId = app.meta.spaces[0]?.id ?? '';
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
