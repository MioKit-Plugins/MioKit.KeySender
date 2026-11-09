<script setup>
import {
  computed,
  onBeforeUnmount,
  onDeactivated,
  ref,
  watch,
} from 'vue';
import { toast } from 'vue-sonner';
import { Badge } from '@/components/ui/badge';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import { cn } from '@/lib/utils';
import { useHotKeyCapture } from '@/composables/useHotKeyCapture';
import HotKeyEditor from './HotKeyEditor.vue';

const props = defineProps({
  display: { type: String, default: '' },
  baselineValueText: { type: String, default: '0' },
  excludeOwnerId: { type: String, default: '' },
  onBeginCapture: { type: Function, default: null },
  onApplyCapture: { type: Function, default: null },
});

const emit = defineEmits(['save']);

const open = ref(false);
const ownsCapture = ref(false);
const {
  captureVm,
  bridgeAvailable,
  beginCapture,
  cancelCapture,
  clearDraft,
} = useHotKeyCapture();

const displayText = computed(() => props.display || '未设置');
const hasHotKey = computed(() => !!props.display);
const captureReady = computed(() =>
  bridgeAvailable && captureVm?._loading === false && !captureVm?._error,
);

function startCaptureSession() {
  ownsCapture.value = true;
  if (typeof props.onBeginCapture === 'function') {
    return props.onBeginCapture();
  }

  return beginCapture(props.baselineValueText, props.excludeOwnerId);
}

function cancelOwnedCapture() {
  if (!ownsCapture.value) {
    return;
  }

  ownsCapture.value = false;
  cancelCapture();
}

function handleOpenChange(nextOpen) {
  if (nextOpen && !captureReady.value) {
    toast.message(
      bridgeAvailable
        ? '快捷键录入服务正在连接，请稍后重试'
        : '快捷键录入需在 WebView2 环境中使用',
    );
    open.value = false;
    return;
  }

  open.value = nextOpen;

  if (nextOpen) {
    startCaptureSession();
    return;
  }

  cancelOwnedCapture();
}

function handleDismiss() {
  handleOpenChange(false);
}

function handleSave() {
  ownsCapture.value = false;
  if (typeof props.onApplyCapture === 'function') {
    props.onApplyCapture();
  }

  emit('save');
  open.value = false;
}

watch(
  () => captureVm?.Properties?.IsCapturing,
  (isCapturing) => {
    if (isCapturing === false && open.value) {
      ownsCapture.value = false;
      open.value = false;
    }
  },
);

function stopCaptureForLifecycle() {
  open.value = false;
  cancelOwnedCapture();
}

onDeactivated(stopCaptureForLifecycle);
onBeforeUnmount(stopCaptureForLifecycle);
</script>

<template>
  <Popover :open="open" @update:open="handleOpenChange">
    <PopoverTrigger as-child>
      <button
        type="button"
        :disabled="!captureReady"
        :class="cn(
          'rounded-md transition-opacity',
          !captureReady && 'cursor-not-allowed opacity-60',
        )"
      >
        <Badge
          variant="outline"
          :class="[
            'min-w-[120px] justify-center px-3 py-1.5 font-mono text-xs transition-colors',
            hasHotKey
              ? 'border-primary/40 bg-primary/10 text-primary'
              : 'text-muted-foreground',
          ]"
        >
          {{ displayText }}
        </Badge>
      </button>
    </PopoverTrigger>

    <PopoverContent
      class="w-[300px] border-border bg-popover p-0 shadow-md"
      align="end"
      side="top"
      :side-offset="8"
      @pointer-down-outside="handleDismiss"
      @focus-outside="handleDismiss"
      @interact-outside="handleDismiss"
      @escape-key-down="handleDismiss"
    >
      <HotKeyEditor
        :capture-vm="captureVm"
        :clear-draft="clearDraft"
        @save="handleSave"
      />
    </PopoverContent>
  </Popover>
</template>
