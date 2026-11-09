<script setup>
import { computed, ref } from 'vue';
import {
  Folder,
  Globe,
  Layers,
  PanelLeftClose,
  PanelLeftOpen,
  Pencil,
  Plus,
  Trash2,
} from 'lucide-vue-next';
import { Button } from '@/components/ui/button';
import {
  LIST_SCOPE_ALL,
  LIST_SCOPE_BUILTIN,
  LIST_SCOPE_CATEGORY,
  NODE_DRAG_MIME,
} from '@/api/key-sender.js';
import { useManagerStore } from '@/stores/useManagerStore';

const store = useManagerStore();
const dropTargetId = ref(null);

const virtualItems = computed(() => [
  {
    key: 'all',
    scope: LIST_SCOPE_ALL,
    categoryId: null,
    title: '全部',
    description: '显示全部节点',
    icon: Globe,
    droppable: false,
  },
  {
    key: 'builtin',
    scope: LIST_SCOPE_BUILTIN,
    categoryId: null,
    title: '系统内置',
    description: '只读内置按键',
    icon: Layers,
    droppable: false,
  },
]);

function isSelected(item) {
  if (item.scope === LIST_SCOPE_CATEGORY) {
    return store.listScope === LIST_SCOPE_CATEGORY
      && !!item.categoryId
      && store.selectedCategoryId === item.categoryId;
  }
  return store.listScope === item.scope;
}

function selectItem(item) {
  store.selectScope(item.scope, item.categoryId);
}

function onDragOver(event, item) {
  if (!item.droppable) return;
  const types = event.dataTransfer?.types ?? [];
  if (![...types].includes(NODE_DRAG_MIME) && ![...types].includes('text/plain')) return;
  event.preventDefault();
  event.dataTransfer.dropEffect = event.ctrlKey ? 'copy' : 'move';
  dropTargetId.value = item.categoryId;
}

function onDragLeave(item) {
  if (dropTargetId.value === item.categoryId) dropTargetId.value = null;
}

async function onDrop(event, item) {
  dropTargetId.value = null;
  if (!item.droppable) return;
  event.preventDefault();
  let payload = null;
  try {
    const raw = event.dataTransfer.getData(NODE_DRAG_MIME)
      || event.dataTransfer.getData('text/plain');
    payload = raw ? JSON.parse(raw) : null;
  } catch {
    payload = null;
  }
  const nodeIds = payload?.nodeIds ?? [];
  if (!nodeIds.length) return;
  await store.dropNodesToCategory(item.categoryId, {
    nodeIds,
    copy: !!event.ctrlKey || !!payload?.copy,
  });
}
</script>

<template>
  <aside
    class="ks-category-sidebar flex h-full shrink-0 flex-col border-r border-border bg-muted/20"
    :class="store.sidebarCollapsed ? 'w-14' : 'w-60'"
  >
    <div class="ks-list-scroll min-h-0 flex-1 space-y-1 overflow-auto p-2">
      <button
        v-for="item in virtualItems"
        :key="item.key"
        type="button"
        class="ks-category-item group relative flex w-full items-center gap-2.5 rounded-lg px-2 py-2 text-left transition-colors"
        :class="isSelected(item)
          ? 'bg-primary/10 text-primary'
          : 'text-foreground hover:bg-muted/70'"
        @click="selectItem(item)"
      >
        <span
          v-if="isSelected(item)"
          class="absolute inset-y-1.5 left-0 w-1 rounded-full bg-primary"
        />
        <span
          class="grid size-8 shrink-0 place-items-center rounded-md"
          :class="isSelected(item) ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground'"
        >
          <component :is="item.icon" class="size-4" />
        </span>
        <span v-if="!store.sidebarCollapsed" class="min-w-0 flex-1">
          <span class="block truncate text-sm font-medium leading-tight">{{ item.title }}</span>
          <span
            class="mt-0.5 block truncate text-[11px]"
            :class="isSelected(item) ? 'text-primary/80' : 'text-muted-foreground'"
          >
            {{ item.description }}
          </span>
        </span>
      </button>

      <div
        v-if="!store.sidebarCollapsed && store.categories.length"
        class="px-2 pb-1 pt-2 text-[11px] font-medium uppercase tracking-wide text-muted-foreground"
      >
        我的分类
      </div>

      <button
        v-for="category in store.categories"
        :key="category.categoryId"
        type="button"
        class="ks-category-item group relative flex w-full items-center gap-2.5 rounded-lg px-2 py-2 text-left transition-colors"
        :class="[
          isSelected({ scope: LIST_SCOPE_CATEGORY, categoryId: category.categoryId })
            ? 'bg-primary/10 text-primary'
            : 'text-foreground hover:bg-muted/70',
          dropTargetId === category.categoryId ? 'ring-2 ring-primary/40' : '',
        ]"
        @click="selectItem({ scope: LIST_SCOPE_CATEGORY, categoryId: category.categoryId })"
        @dragover="onDragOver($event, { droppable: true, categoryId: category.categoryId })"
        @dragleave="onDragLeave({ categoryId: category.categoryId })"
        @drop="onDrop($event, { droppable: true, categoryId: category.categoryId })"
      >
        <span
          v-if="isSelected({ scope: LIST_SCOPE_CATEGORY, categoryId: category.categoryId })"
          class="absolute inset-y-1.5 left-0 w-1 rounded-full bg-primary"
        />
        <span
          class="grid size-8 shrink-0 place-items-center rounded-md"
          :class="isSelected({ scope: LIST_SCOPE_CATEGORY, categoryId: category.categoryId })
            ? 'bg-primary text-primary-foreground'
            : 'bg-muted text-muted-foreground'"
        >
          <Folder class="size-4" />
        </span>
        <span v-if="!store.sidebarCollapsed" class="min-w-0 flex-1">
          <span class="block truncate text-sm font-medium leading-tight">{{ category.name }}</span>
          <span
            class="mt-0.5 block truncate text-[11px]"
            :class="isSelected({ scope: LIST_SCOPE_CATEGORY, categoryId: category.categoryId })
              ? 'text-primary/80'
              : 'text-muted-foreground'"
          >
            {{ category.nodeCount }} 个节点
            <template v-if="category.description"> · {{ category.description }}</template>
          </span>
        </span>
      </button>
    </div>

    <div class="flex shrink-0 items-center justify-between gap-1 border-t border-border px-2 py-1.5">
      <div class="flex items-center gap-0.5">
        <Button
          size="icon"
          variant="ghost"
          class="size-8"
          @click="store.openCreateCategoryDialog()"
        >
          <Plus class="size-4" />
        </Button>
        <Button
          size="icon"
          variant="ghost"
          class="size-8"
          :disabled="!store.canEditSelectedCategory"
          @click="store.openRenameCategoryDialog()"
        >
          <Pencil class="size-3.5" />
        </Button>
        <Button
          size="icon"
          variant="ghost"
          class="size-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
          :disabled="!store.canDeleteSelectedCategory"
          @click="store.requestDeleteCategory()"
        >
          <Trash2 class="size-3.5" />
        </Button>
      </div>
      <Button
        size="icon"
        variant="ghost"
        class="size-8"
        @click="store.toggleSidebarCollapsed()"
      >
        <PanelLeftClose v-if="!store.sidebarCollapsed" class="size-4" />
        <PanelLeftOpen v-else class="size-4" />
      </Button>
    </div>
  </aside>
</template>
