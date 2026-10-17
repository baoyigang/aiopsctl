import { onBeforeUnmount, ref } from 'vue';

/** One active read per page: cancel the old request and ignore stale completions. */
export function useRequest() {
  const loading = ref(false);
  const error = ref('');
  let generation = 0;
  let controller: AbortController | undefined;
  async function run<T>(
    fetcher: (signal: AbortSignal) => Promise<T>,
    accept: (result: T) => void,
  ): Promise<void> {
    const current = ++generation;
    controller?.abort('superseded');
    controller = new AbortController();
    loading.value = true;
    error.value = '';
    try {
      const result = await fetcher(controller.signal);
      if (current === generation) accept(result);
    } catch (value) {
      if (current === generation && !(value instanceof DOMException && value.name === 'AbortError'))
        error.value = value instanceof Error ? value.message : '请求未完成。';
    } finally {
      if (current === generation) loading.value = false;
    }
  }
  function cancel() {
    generation++;
    controller?.abort('cancelled');
    loading.value = false;
  }
  onBeforeUnmount(cancel);
  return { loading, error, run, cancel };
}
