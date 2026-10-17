import { computed, onBeforeUnmount, onMounted, ref, watch, type Ref } from 'vue';

interface AutoRefreshOptions {
  intervalSeconds: Ref<number>;
  isBusy: () => boolean;
  /** The caller owns request cancellation and error presentation. */
  refresh: () => Promise<void>;
}

export function useAutoRefresh(options: AutoRefreshOptions): {
  remaining: Ref<number>;
  paused: Ref<boolean>;
  reset: () => void;
} {
  // Only the intervals offered by the UI can enable background reads.
  const interval = computed(() =>
    options.intervalSeconds.value === 30 || options.intervalSeconds.value === 60
      ? options.intervalSeconds.value
      : 0,
  );
  const hidden = ref(typeof document !== 'undefined' && document.hidden);
  const refreshing = ref(false);
  const remaining = ref(interval.value);
  const paused = computed(
    () => interval.value === 0 || hidden.value || refreshing.value || options.isBusy(),
  );
  let timer: ReturnType<typeof setInterval> | undefined;
  let mounted = false;
  let disposed = false;

  function stop() {
    if (timer !== undefined) clearInterval(timer);
    timer = undefined;
  }

  async function tick() {
    if (disposed || interval.value <= 0 || paused.value || options.isBusy()) return;
    remaining.value = Math.max(0, remaining.value - 1);
    if (remaining.value > 0) return;
    // Set the guard before invoking refresh, even if isBusy is not reactive.
    refreshing.value = true;
    remaining.value = interval.value;
    try {
      await options.refresh();
    } catch {
      // A failed read must not create an unhandled timer rejection or a retry loop.
      // The supplied request handler should display its error to the user.
    } finally {
      if (!disposed) {
        refreshing.value = false;
        remaining.value = interval.value;
      }
    }
  }

  function reset() {
    stop();
    remaining.value = interval.value;
    if (mounted && !disposed && !hidden.value && interval.value > 0)
      timer = setInterval(() => void tick(), 1000);
  }

  function onVisibilityChange() {
    hidden.value = document.hidden;
    reset();
  }

  watch(interval, reset, { flush: 'sync' });
  onMounted(() => {
    mounted = true;
    hidden.value = document.hidden;
    document.addEventListener('visibilitychange', onVisibilityChange);
    reset();
  });
  onBeforeUnmount(() => {
    disposed = true;
    mounted = false;
    stop();
    document.removeEventListener('visibilitychange', onVisibilityChange);
  });

  return { remaining, paused, reset };
}
