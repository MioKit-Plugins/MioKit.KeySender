<script setup>
import { computed } from 'vue';
import { chordSummary, keyCapLabel } from '@/api/types.js';
import { cn } from '@/lib/utils';

const props = defineProps({
  events: {
    type: Array,
    default: () => [],
  },
});

const summary = computed(() => chordSummary(props.events));

const steps = computed(() =>
  (props.events ?? []).map((ev, index) => ({
    id: ev.eventId || `${ev.key}-${ev.type}-${index}`,
    label: keyCapLabel(ev.key),
    isDown: ev.type === 'keyDown',
  })));

const parts = computed(() => {
  if (!summary.value) return [];
  return summary.value.split(' + ').filter(Boolean);
});
</script>

<template>
  <div
    v-if="steps.length"
    class="overflow-hidden rounded-xl border border-border/70 bg-gradient-to-br from-muted/50 via-background to-muted/20"
  >
    <div class="border-b border-border/50 px-4 py-3.5">
      <p class="text-[11px] font-medium tracking-[0.16em] text-muted-foreground uppercase">
        按键预览
      </p>
      <div class="mt-2 flex flex-wrap items-center gap-1.5">
        <template v-if="parts.length">
          <template v-for="(part, i) in parts" :key="`${part}-${i}`">
            <span
              class="inline-flex items-center rounded-md border border-border/70 bg-background/80 px-2 py-0.5 text-[0.95rem] font-semibold tracking-wide shadow-sm"
            >
              {{ part }}
            </span>
            <span
              v-if="i < parts.length - 1"
              class="px-0.5 text-sm text-muted-foreground/60"
            >+</span>
          </template>
        </template>
        <span v-else class="text-sm text-muted-foreground">
          {{ steps.length }} 个事件
        </span>
      </div>
    </div>

    <div class="px-4 py-4">
      <div class="flex flex-wrap items-center gap-x-1.5 gap-y-3">
        <template v-for="(step, index) in steps" :key="step.id">
          <div
            :class="cn(
              'relative inline-flex h-11 min-w-11 items-center justify-center rounded-[10px] border px-3',
              'bg-gradient-to-b from-card to-muted/40 text-card-foreground shadow-[0_1px_0_hsl(var(--border))]',
              step.isDown
                ? 'border-primary/40 ring-1 ring-primary/20'
                : 'border-border/70',
            )"
          >
            <span class="text-[13px] font-semibold tracking-wide select-none">
              {{ step.label }}
            </span>
            <span
              :class="cn(
                'absolute -right-1 -bottom-1 flex h-[18px] min-w-[18px] items-center justify-center rounded-full px-1',
                'border text-[9px] font-bold leading-none shadow-sm',
                step.isDown
                  ? 'border-primary/50 bg-primary text-primary-foreground'
                  : 'border-border bg-muted text-muted-foreground',
              )"
              :title="step.isDown ? '按下' : '抬起'"
            >
              {{ step.isDown ? '↓' : '↑' }}
            </span>
          </div>

          <span
            v-if="index < steps.length - 1"
            class="mx-0.5 text-muted-foreground/40 select-none"
            aria-hidden="true"
          >›</span>
        </template>
      </div>
    </div>
  </div>
</template>
