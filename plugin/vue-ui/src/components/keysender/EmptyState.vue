<script setup>
import { cn } from '@/lib/utils';

defineProps({
  title: { type: String, required: true },
  description: { type: String, default: '' },
  class: { type: null, required: false },
});
</script>

<template>
  <div
    :class="cn(
      'flex min-h-48 flex-1 flex-col items-center justify-center gap-3 px-6 py-12 text-center',
      $props.class,
    )"
  >
    <span
      v-if="$slots.icon"
      class="flex size-11 items-center justify-center rounded-xl bg-muted text-muted-foreground ring-1 ring-border/60"
    >
      <slot name="icon" />
    </span>
    <div class="space-y-1.5">
      <p class="text-sm font-medium text-foreground">{{ title }}</p>
      <p v-if="description" class="max-w-sm text-xs leading-5 text-muted-foreground">
        {{ description }}
      </p>
    </div>
    <div v-if="$slots.default" class="flex flex-wrap items-center justify-center gap-2 pt-1">
      <slot />
    </div>
  </div>
</template>
