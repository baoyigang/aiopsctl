<script setup lang="ts">
defineProps<{ title?: string; description?: string; error?: string }>();
defineEmits<{ retry: [] }>();
</script>
<template>
  <div class="empty-state" :role="error ? 'alert' : undefined">
    <div class="empty-icon">{{ error ? '!' : '▱' }}</div>
    <h3>{{ error ? '数据暂未加载' : (title ?? '暂无数据') }}</h3>
    <p>{{ error || description || '调整筛选条件或新建数据后查看。' }}</p>
    <button v-if="error" class="button secondary small" @click="$emit('retry')">重新加载</button>
  </div>
</template>
