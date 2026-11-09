<script setup>
import { computed } from 'vue';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { keyDisplay } from '@/api/types.js';
import { useManagerStore } from '@/stores/useManagerStore';

const store = useManagerStore();

const open = computed({
  get: () => store.dialogs.confirmSequence,
  set: (v) => {
    if (!v) store.cancelSequenceConfirm();
  },
});

const warnings = computed(() => store.confirmContext?.warnings ?? []);

const title = computed(() => {
  const mode = store.confirmContext?.mode;
  if (mode === 'copy') return '复制前确认序列警告';
  if (mode === 'import') return '导入前确认序列警告';
  return '保存前确认序列警告';
});
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="sm:max-w-lg">
      <DialogHeader>
        <DialogTitle>{{ title }}</DialogTitle>
        <DialogDescription>
          正常结束不会自动释放；取消、失败或点击「释放全部」时会清理。确认后不会自动补抬起事件。
        </DialogDescription>
      </DialogHeader>

      <ul class="max-h-56 space-y-2 overflow-y-auto text-sm">
        <li
          v-for="(w, i) in warnings"
          :key="`${w.code}-${w.eventIndex}-${i}`"
          class="rounded-md border border-border px-3 py-2"
        >
          <div class="font-medium">
            {{ keyDisplay(w.key) || w.key }}
            <span class="text-muted-foreground">· 事件 #{{ (w.eventIndex ?? 0) + 1 }}</span>
          </div>
          <div class="text-muted-foreground">{{ w.message }}</div>
        </li>
      </ul>

      <DialogFooter class="gap-2 sm:justify-end">
        <Button variant="outline" class="min-w-24" @click="store.cancelSequenceConfirm()">
          返回修改
        </Button>
        <Button class="min-w-24" @click="store.confirmSequenceSave()">
          仍然保存
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
