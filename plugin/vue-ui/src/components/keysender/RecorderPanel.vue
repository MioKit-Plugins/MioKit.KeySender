<script setup>
import { computed } from 'vue';
import { Circle, Square, X } from 'lucide-vue-next';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { useManagerStore } from '@/stores/useManagerStore';
import KeySequencePreview from './KeySequencePreview.vue';

const store = useManagerStore();

const stateLabel = computed(() => {
  const s = store.recording?.state;
  if (!s) return '空闲';
  const map = {
    recording: '录制中',
    review: '写入中',
    cancelled: '已取消',
    failed: '失败',
  };
  return map[s] ?? s;
});

const isRecording = computed(() =>
  store.recording?.state === 'recording'
);

const canStart = computed(() =>
  !store.draftReadOnly && !store.recording && !store.recordingBusy);

const canStop = computed(() =>
  store.recording?.state === 'recording' && !store.recordingBusy);

const canCancel = computed(() =>
  !!store.recording && !store.recordingBusy);

const liveCount = computed(() =>
  store.recordingFragment?.events?.length
    ?? store.recording?.eventCount
    ?? 0);

const previewEvents = computed(() => store.draft?.sequence?.events ?? []);

const notices = computed(() => store.recording?.notices ?? store.recordingFragment?.notices ?? []);
</script>

<template>
  <div class="space-y-3">
    <div class="rounded-xl border border-border bg-muted/20 p-3">
      <div class="flex min-h-9 flex-wrap items-center gap-2">
        <div class="mr-1 flex items-center gap-2">
          <span class="text-sm font-medium">录制</span>
          <Badge
            variant="outline"
            :class="[
              'min-w-14 justify-center tabular-nums',
              isRecording ? 'border-destructive/40 text-destructive' : '',
            ]"
          >
            {{ stateLabel }}
          </Badge>
          <span
            v-if="isRecording"
            class="text-xs tabular-nums text-muted-foreground"
          >
            {{ liveCount }} 条
          </span>
        </div>

        <Button
          size="sm"
          class="min-w-24 gap-1.5"
          :disabled="!canStart"
          @click="store.beginRecording()"
        >
          <Circle class="size-3 fill-current" />
          开始录制
        </Button>
        <Button
          size="sm"
          variant="secondary"
          class="min-w-24 gap-1.5"
          :disabled="!canStop"
          @click="store.stopRecording()"
        >
          <Square class="size-3 fill-current" />
          停止录制
        </Button>
        <Button
          size="sm"
          variant="ghost"
          class="min-w-24 gap-1.5"
          :disabled="!canCancel"
          @click="store.cancelRecording()"
        >
          <X class="size-3.5" />
          取消录制
        </Button>

        <template v-if="store.isMock && isRecording">
          <span class="text-xs text-muted-foreground">Mock 注入：</span>
          <Button size="sm" variant="outline" class="min-w-20" @click="store.injectMockKey('keyDown', 'LeftShift')">
            ↓ Shift
          </Button>
          <Button size="sm" variant="outline" class="min-w-20" @click="store.injectMockKey('keyUp', 'LeftShift')">
            ↑ Shift
          </Button>
          <Button size="sm" variant="outline" class="min-w-16" @click="store.injectMockKey('keyDown', 'KeyA')">
            ↓ A
          </Button>
          <Button size="sm" variant="outline" class="min-w-16" @click="store.injectMockKey('keyUp', 'KeyA')">
            ↑ A
          </Button>
        </template>
      </div>

      <p
        v-if="notices.length && isRecording"
        class="mt-2 text-xs leading-5 text-muted-foreground"
      >
        {{ notices.map((n) => n.message || n.code).join('；') }}
      </p>
    </div>

    <KeySequencePreview :events="previewEvents" />
  </div>
</template>
