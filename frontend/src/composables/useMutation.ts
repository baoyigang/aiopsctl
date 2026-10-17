import { ref } from 'vue';
import { ApiError } from '../api';
import { loadMeta, app } from '../context';
export function useMutation() {
  const saving = ref(false),
    error = ref(''),
    conflict = ref(false);
  async function mutate<T>(
    fn: () => Promise<T>,
    accept: (value: T) => void | Promise<void>,
  ): Promise<void> {
    if (saving.value) return;
    saving.value = true;
    error.value = '';
    conflict.value = false;
    try {
      await accept(await fn());
    } catch (value) {
      error.value = value instanceof Error ? value.message : '操作未完成。';
      conflict.value = value instanceof ApiError && value.status === 409;
    } finally {
      saving.value = false;
    }
  }
  async function refreshRevision(accept: (revision: number) => void) {
    await mutate(
      () => loadMeta(),
      () => {
        accept(app.meta!.revision);
        error.value = '已刷新版本并保留草稿。请核对内容后再保存。';
      },
    );
  }
  function reset() {
    error.value = '';
    conflict.value = false;
  }
  return { saving, error, conflict, mutate, refreshRevision, reset };
}
