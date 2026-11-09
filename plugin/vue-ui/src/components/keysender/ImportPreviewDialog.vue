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
import { Badge } from '@/components/ui/badge';
import { ScrollArea } from '@/components/ui/scroll-area';
import { useManagerStore } from '@/stores/useManagerStore';

const store = useManagerStore();

const open = computed({
  get: () => store.dialogs.importPreview,
  set: (v) => {
    if (!v) store.cancelImport();
  },
});

const preview = computed(() => store.importPreview);
const hasHardErrors = computed(() =>
  (preview.value?.items ?? []).some((i) => (i.errors?.length ?? 0) > 0));
const hasWarnings = computed(() =>
  (preview.value?.items ?? []).some((i) => (i.warnings?.length ?? 0) > 0));

async function onCommit() {
  const fp = hasWarnings.value ? (preview.value?.warningFingerprint ?? null) : null;
  await store.commitImport(fp);
}
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="sm:max-w-xl">
      <DialogHeader>
        <DialogTitle>导入预览</DialogTitle>
        <DialogDescription>
          硬错误将阻止整包导入。仅有配对警告时需明确确认后创建副本。
        </DialogDescription>
      </DialogHeader>

      <ScrollArea class="max-h-72">
        <ul class="space-y-2 pr-3">
          <li
            v-for="item in preview?.items ?? []"
            :key="item.itemIndex"
            class="rounded-md border border-border px-3 py-2 text-sm"
          >
            <div class="flex items-center gap-2 font-medium">
              <span class="truncate">{{ item.name }}</span>
              <Badge v-if="item.errors?.length" variant="destructive">错误</Badge>
              <Badge v-else-if="item.warnings?.length" variant="secondary">警告</Badge>
            </div>
            <div class="text-xs text-muted-foreground">
              {{ item.eventCount }} 条事件 · 配置等待 {{ item.totalDelayMs }} ms
            </div>
            <div v-if="item.description" class="truncate text-xs text-muted-foreground">
              {{ item.description }}
            </div>
            <ul v-if="item.errors?.length" class="mt-1 text-xs text-destructive">
              <li v-for="(e, i) in item.errors" :key="i">{{ e.message || e.code }}</li>
            </ul>
            <ul v-if="item.warnings?.length" class="mt-1 text-xs text-warning-foreground dark:text-warning">
              <li v-for="(w, i) in item.warnings" :key="i">{{ w.message || w.code }}</li>
            </ul>
          </li>
        </ul>
      </ScrollArea>

      <DialogFooter class="gap-2">
        <Button variant="outline" class="min-w-20" @click="store.cancelImport()">取消</Button>
        <Button
          class="min-w-40"
          :disabled="!preview?.canImport || hasHardErrors"
          @click="onCommit"
        >
          {{ hasWarnings ? '确认警告并创建副本' : '创建副本' }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
