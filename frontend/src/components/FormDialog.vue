<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref } from 'vue';
const props = defineProps<{ title: string; busy?: boolean; wide?: boolean }>();
const emit = defineEmits<{ close: [] }>();
const closeButton = ref<HTMLButtonElement | null>(null);
let previous: HTMLElement | null = null;
onMounted(async () => {
  previous = document.activeElement as HTMLElement | null;
  await nextTick();
  closeButton.value?.focus();
});
onBeforeUnmount(() => previous?.focus());
function key(event: KeyboardEvent) {
  if (event.key === 'Escape' && !props.busy) emit('close');
  if (event.key !== 'Tab') return;
  const items = (event.currentTarget as HTMLElement).querySelectorAll<HTMLElement>(
    'button:not(:disabled),input:not(:disabled),textarea:not(:disabled),select:not(:disabled),a[href]',
  );
  if (event.shiftKey && document.activeElement === items[0]) {
    event.preventDefault();
    items[items.length - 1]?.focus();
  } else if (!event.shiftKey && document.activeElement === items[items.length - 1]) {
    event.preventDefault();
    items[0]?.focus();
  }
}
</script>
<template>
  <div class="dialog-backdrop" @click.self="!busy && emit('close')">
    <section
      class="dialog"
      :class="{ wide }"
      role="dialog"
      aria-modal="true"
      aria-labelledby="form-dialog-title"
      @keydown="key"
    >
      <header>
        <h2 id="form-dialog-title">{{ title }}</h2>
        <button
          ref="closeButton"
          class="icon-button"
          aria-label="关闭弹窗"
          :disabled="busy"
          @click="emit('close')"
        >
          ×
        </button>
      </header>
      <div class="dialog-content"><slot /></div>
    </section>
  </div>
</template>
