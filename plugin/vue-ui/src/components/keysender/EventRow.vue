<script setup>
import { computed } from 'vue';
import { GripVertical, Copy, Trash2 } from 'lucide-vue-next';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { EVENT_TYPE_LABELS } from '@/api/types.js';
import KeyPicker from './KeyPicker.vue';

const props = defineProps({
  index: { type: Number, required: true },
  event: { type: Object, required: true },
  readOnly: { type: Boolean, default: false },
  warningText: { type: String, default: '' },
  errorText: { type: String, default: '' },
});

const emit = defineEmits([
  'update',
  'duplicate',
  'remove',
]);

const delayInvalid = computed(() => {
  const n = Number(props.event.delayBeforeMs);
  return !Number.isInteger(n) || n < 0 || n > 600000;
});
</script>

<template>
  <div
    class="grid grid-cols-[1.5rem_2rem_6.5rem_minmax(7.5rem,1fr)_6.5rem_auto] items-start gap-1 border-b border-border/60 px-1 py-1.5"
  >
    <button
      type="button"
      class="mt-1.5 inline-flex size-5 cursor-grab items-center justify-center text-muted-foreground disabled:opacity-40"
      :disabled="readOnly"
      aria-label="拖动排序"
      data-drag-handle
    >
      <GripVertical class="size-3.5" />
    </button>

    <div class="mt-1.5 text-center text-xs tabular-nums text-muted-foreground">
      {{ index + 1 }}
    </div>

    <Select
      :model-value="event.type"
      :disabled="readOnly"
      @update:model-value="(v) => emit('update', { type: v })"
    >
      <SelectTrigger class="h-8">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="keyDown">{{ EVENT_TYPE_LABELS.keyDown }}</SelectItem>
        <SelectItem value="keyUp">{{ EVENT_TYPE_LABELS.keyUp }}</SelectItem>
      </SelectContent>
    </Select>

    <KeyPicker
      :model-value="event.key"
      :disabled="readOnly"
      @update:model-value="(v) => emit('update', { key: v })"
    />

    <div class="relative">
      <Input
        class="h-8 pr-8 tabular-nums"
        type="number"
        min="0"
        max="600000"
        step="1"
        :disabled="readOnly"
        :aria-invalid="delayInvalid"
        :model-value="event.delayBeforeMs"
        @update:model-value="(v) => emit('update', { delayBeforeMs: Number(v) })"
      />
      <span class="pointer-events-none absolute top-1/2 right-2 -translate-y-1/2 text-[10px] text-muted-foreground">
        ms
      </span>
    </div>

    <div class="flex items-center gap-0.5">
      <Button variant="ghost" size="icon-sm" :disabled="readOnly" aria-label="复制行" @click="emit('duplicate')">
        <Copy class="size-3.5" />
      </Button>
      <Button variant="ghost" size="icon-sm" :disabled="readOnly" aria-label="删除行" @click="emit('remove')">
        <Trash2 class="size-3.5" />
      </Button>
    </div>

    <div class="col-span-full min-h-4 px-1 text-[11px] leading-4">
      <span v-if="errorText" class="text-destructive">{{ errorText }}</span>
      <span v-else-if="warningText" class="text-warning-foreground/90 dark:text-warning">{{ warningText }}</span>
      <span v-else class="invisible">占位</span>
    </div>
  </div>
</template>
