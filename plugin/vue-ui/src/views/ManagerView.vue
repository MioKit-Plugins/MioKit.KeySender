<script setup>
import { computed, onMounted, onUnmounted, ref, watch } from 'vue';
import {
  Download,
  Keyboard,
  Plus,
  Search,
  Trash2,
  Upload,
} from 'lucide-vue-next';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { LIST_SCOPE_BUILTIN, LIST_SCOPE_CATEGORY } from '@/api/key-sender.js';
import { useManagerStore } from '@/stores/useManagerStore';
import CategorySidebar from '@/components/keysender/CategorySidebar.vue';
import NodeList from '@/components/keysender/NodeList.vue';
import NodeEditSheet from '@/components/keysender/NodeEditSheet.vue';
import ConfirmSequenceDialog from '@/components/keysender/ConfirmSequenceDialog.vue';
import DirtyLeaveDialog from '@/components/keysender/DirtyLeaveDialog.vue';
import ImportPreviewDialog from '@/components/keysender/ImportPreviewDialog.vue';
import DeleteConfirmDialog from '@/components/keysender/DeleteConfirmDialog.vue';
import DeleteCategoryDialog from '@/components/keysender/DeleteCategoryDialog.vue';
import CategoryEditDialog from '@/components/keysender/CategoryEditDialog.vue';
import ReleaseAndRecordDialog from '@/components/keysender/ReleaseAndRecordDialog.vue';

const store = useManagerStore();
const searchText = ref(store.query);
let searchTimer = 0;

const scopeLabel = computed(() => {
  if (store.listScope === LIST_SCOPE_BUILTIN) return '系统内置';
  if (store.listScope === LIST_SCOPE_CATEGORY) {
    return store.selectedCategory?.name || '分类';
  }
  return '全部';
});

watch(
  () => store.query,
  (v) => {
    if (v !== searchText.value) searchText.value = v;
  },
);

onMounted(() => {
  store.bootstrap();
});

onUnmounted(() => {
  store.dispose();
  window.clearTimeout(searchTimer);
});

function applySearch() {
  window.clearTimeout(searchTimer);
  store.setQuery(searchText.value.trim());
}

function onSearchInput(value) {
  searchText.value = value;
  window.clearTimeout(searchTimer);
  searchTimer = window.setTimeout(() => {
    store.setQuery(searchText.value.trim());
  }, 280);
}
</script>

<template>
  <div class="flex h-screen w-full overflow-hidden bg-background text-foreground">
    <CategorySidebar />

    <div class="flex min-h-0 min-w-0 flex-1 flex-col gap-4 p-5">
      <header class="flex shrink-0 items-start justify-between gap-4">
        <div class="flex min-w-0 items-start gap-3">
          <span
            class="mt-0.5 flex size-10 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary ring-1 ring-primary/15"
          >
            <Keyboard class="size-5" />
          </span>
          <div class="min-w-0 space-y-1">
            <div class="flex flex-wrap items-center gap-2">
              <h1 class="text-base font-semibold tracking-tight">
                {{ store.displayName }}
              </h1>
              <Badge v-if="store.isMock" variant="secondary">Mock</Badge>
              <Badge variant="outline">{{ scopeLabel }}</Badge>
            </div>
            <p class="text-sm text-muted-foreground">
              用录制快速创建按键序列，需要时再精细调整顺序与间隔。
            </p>
          </div>
        </div>
      </header>

      <section class="ks-shell min-h-0 flex-1">
        <div class="ks-shell-toolbar">
          <div class="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
            <div class="relative min-w-0 flex-1 sm:max-w-md">
              <Search
                class="pointer-events-none absolute top-1/2 left-2.5 size-3.5 -translate-y-1/2 text-muted-foreground"
              />
              <Input
                :model-value="searchText"
                class="h-9 pl-8"
                :placeholder="`在「${scopeLabel}」中搜索名称、描述或别名…`"
                @update:model-value="onSearchInput"
                @keydown.enter="applySearch"
              />
            </div>

            <div class="flex flex-wrap items-center gap-2">
              <Button
                class="h-9 gap-1.5"
                size="sm"
                :disabled="!store.canCreateNode"
                @click="store.requestNewNode()"
              >
                <Plus class="size-4" />
                新建
              </Button>
              <Button
                variant="outline"
                class="h-9 gap-1.5"
                size="sm"
                @click="store.openImport()"
              >
                <Upload class="size-4" />
                导入
              </Button>
              <Button
                variant="outline"
                class="h-9 gap-1.5"
                size="sm"
                @click="store.exportSelected()"
              >
                <Download class="size-4" />
                导出
              </Button>
              <Button
                variant="outline"
                size="icon"
                class="size-9 text-destructive hover:bg-destructive/10 hover:text-destructive"
                :disabled="!store.canBatchDelete"
                :title="store.canBatchDelete ? undefined : store.batchDeleteBlockedReason"
                @click="store.requestDelete()"
              >
                <Trash2 class="size-4" />
              </Button>
            </div>
          </div>
        </div>

        <NodeList />
      </section>
    </div>

    <NodeEditSheet />
    <ConfirmSequenceDialog />
    <DirtyLeaveDialog />
    <ImportPreviewDialog />
    <DeleteConfirmDialog />
    <DeleteCategoryDialog />
    <CategoryEditDialog />
    <ReleaseAndRecordDialog />
  </div>
</template>
