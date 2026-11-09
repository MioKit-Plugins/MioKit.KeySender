<script setup>
import { computed } from 'vue';
import { AlertOctagon, BrushCleaning, Save } from 'lucide-vue-next';
import { Button } from '@/components/ui/button';
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip';
import { cn } from '@/lib/utils';

const props = defineProps({
  captureVm: { type: Object, required: true },
  clearDraft: { type: Function, required: true },
});

const emit = defineEmits(['save']);

const draftDisplay = computed(() => {
  const text = props.captureVm?.Properties?.DraftDisplay;
  return text || '—';
});

const editorStatus = computed(() => props.captureVm?.Properties?.EditorStatus ?? 'Initializing');

const errorMessage = computed(() => props.captureVm?.Properties?.ErrorMessage ?? '');

const canSave = computed(() =>
  editorStatus.value === 'CheckSuccess' || editorStatus.value === 'CheckFailure',
);

const containerClass = computed(() =>
  cn(
    'overflow-hidden rounded-md border border-border',
    editorStatus.value === 'CheckSuccess' && 'bg-gradient-to-b from-green-600/20 to-background/80',
    editorStatus.value === 'CheckFailure' && 'bg-gradient-to-b from-yellow-600/20 to-background/80',
    editorStatus.value === 'Initializing' && 'bg-gradient-to-b from-background to-background/80',
  ),
);

function handleClear() {
  props.clearDraft();
}

function handleSave() {
  if (!canSave.value) {
    return;
  }

  emit('save');
}
</script>

<template>
  <div :class="containerClass">
    <div class="flex min-h-[100px] items-center justify-center px-4 py-6">
      <span class="font-mono text-sm font-bold text-foreground">{{ draftDisplay }}</span>
      <TooltipProvider v-if="editorStatus === 'CheckFailure' && errorMessage">
        <Tooltip>
          <TooltipTrigger as-child>
            <span class="ml-2 inline-flex text-destructive">
              <AlertOctagon class="size-4" stroke-width="2.5" />
            </span>
          </TooltipTrigger>
          <TooltipContent>
            <p>{{ errorMessage }}</p>
          </TooltipContent>
        </Tooltip>
      </TooltipProvider>
    </div>

    <div class="border-t border-border" />

    <div class="flex items-center gap-2 px-3 py-2.5">
      <span class="relative flex size-2.5">
        <span class="absolute inline-flex size-full animate-ping rounded-full bg-green-500 opacity-75" />
        <span class="relative inline-flex size-2.5 rounded-full bg-green-500" />
      </span>
      <span class="text-xs font-semibold text-green-600 dark:text-green-400">按下按键...</span>

      <div class="ml-auto flex items-center gap-1.5">
        <Button
          type="button"
          variant="outline"
          size="sm"
          class="h-7 gap-1 px-2 text-xs"
          @click="handleClear"
        >
          <BrushCleaning class="size-3.5" />
          重置
        </Button>
        <Button
          type="button"
          size="sm"
          class="h-7 gap-1 px-2 text-xs"
          :disabled="!canSave"
          @click="handleSave"
        >
          <Save class="size-3.5" />
          保存
        </Button>
      </div>
    </div>
  </div>
</template>
