<script setup>
import { computed } from 'vue';
import {
  ChevronLeft,
  ChevronRight,
  Copy,
  Keyboard,
  Pencil,
  Search,
  Trash2,
} from 'lucide-vue-next';
import { Checkbox } from '@/components/ui/checkbox';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { NODE_DRAG_MIME } from '@/api/key-sender.js';
import { useManagerStore } from '@/stores/useManagerStore';
import EmptyState from './EmptyState.vue';

const store = useManagerStore();

const pageCount = computed(() =>
  Math.max(1, Math.ceil((store.total || 0) / (store.pageSize || 20))));

const allChecked = computed(() =>
  store.nodes.length > 0 && store.nodes.every((n) => store.selectedIds.includes(n.nodeId)));

const showSkeleton = computed(() => store.loading && store.nodes.length === 0);
const isFilterEmpty = computed(() =>
  !store.loading && store.nodes.length === 0 && !!store.query.trim());
const isEmpty = computed(() =>
  !store.loading && store.nodes.length === 0 && !store.query.trim());

function onRowClick(node) {
  store.requestSelectNode(node.nodeId);
}

function onEdit(node, event) {
  event.stopPropagation();
  store.requestSelectNode(node.nodeId);
}

function onCopy(node, event) {
  event.stopPropagation();
  store.copyNode(node.nodeId);
}

function onDelete(node, event) {
  event.stopPropagation();
  store.requestDelete([node.nodeId]);
}

function clearFilter() {
  store.setQuery('');
}

function createDragGhost(node, count, isCopy) {
  const ghost = document.createElement('div');
  ghost.className = 'ks-drag-ghost';
  ghost.setAttribute('aria-hidden', 'true');

  const icon = document.createElement('span');
  icon.className = 'ks-drag-ghost__icon';
  icon.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="20" height="16" x="2" y="4" rx="2" ry="2"/><path d="M6 8h.01"/><path d="M10 8h.01"/><path d="M14 8h.01"/><path d="M18 8h.01"/><path d="M8 12h.01"/><path d="M12 12h.01"/><path d="M16 12h.01"/><path d="M7 16h10"/></svg>';

  const label = document.createElement('span');
  label.className = 'ks-drag-ghost__label';
  label.textContent = node.name || '未命名';

  ghost.append(icon, label);

  if (count > 1) {
    const badge = document.createElement('span');
    badge.className = 'ks-drag-ghost__meta';
    badge.textContent = String(count);
    ghost.append(badge);
  }

  if (isCopy) {
    const copyHint = document.createElement('span');
    copyHint.className = 'ks-drag-ghost__meta';
    copyHint.textContent = '复制';
    ghost.append(copyHint);
  }

  document.body.appendChild(ghost);
  return ghost;
}

function onRowDragStart(node, event) {
  const selected = store.selectedIds.includes(node.nodeId)
    ? [...store.selectedIds]
    : [node.nodeId];
  const payload = JSON.stringify({
    nodeIds: selected,
    copy: !!event.ctrlKey,
  });
  event.dataTransfer.effectAllowed = 'copyMove';
  event.dataTransfer.setData(NODE_DRAG_MIME, payload);
  event.dataTransfer.setData('text/plain', payload);

  const ghost = createDragGhost(node, selected.length, !!event.ctrlKey);
  const rect = ghost.getBoundingClientRect();
  event.dataTransfer.setDragImage(ghost, Math.round(rect.width / 2), Math.round(rect.height / 2));
  requestAnimationFrame(() => ghost.remove());
}
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col overflow-hidden">
    <div v-if="showSkeleton" class="space-y-0 overflow-auto p-3">
      <div
        v-for="i in 5"
        :key="i"
        class="flex items-center gap-3 border-b border-border/70 px-2 py-3 last:border-b-0"
      >
        <Skeleton class="size-4 rounded" />
        <Skeleton class="size-9 rounded-lg" />
        <div class="min-w-0 flex-1 space-y-2">
          <Skeleton class="h-3.5 w-40" />
          <Skeleton class="h-3 w-64 max-w-full" />
        </div>
        <Skeleton class="h-6 w-20 rounded-md" />
        <Skeleton class="ml-auto h-8 w-24" />
      </div>
    </div>

    <EmptyState
      v-else-if="isEmpty"
      title="还没有按键序列"
      description="新建后优先用录制录入；需要调整顺序或间隔时，再打开详细事件表。"
    >
      <template #icon>
        <Keyboard class="size-5" />
      </template>
      <Button class="gap-1.5" size="sm" @click="store.requestNewNode()">
        新建序列
      </Button>
      <Button size="sm" variant="outline" @click="store.openImport()">
        导入
      </Button>
    </EmptyState>

    <EmptyState
      v-else-if="isFilterEmpty"
      title="没有匹配的节点"
      description="换个关键词试试，或清空搜索查看全部列表。"
    >
      <template #icon>
        <Search class="size-5" />
      </template>
      <Button size="sm" variant="outline" @click="clearFilter">
        清空搜索
      </Button>
    </EmptyState>

    <template v-else>
      <div class="ks-list-scroll min-h-0 flex-1 overflow-auto">
        <Table>
          <TableHeader class="sticky top-0 z-10 bg-card">
            <TableRow class="hover:bg-transparent">
              <TableHead class="h-11 w-12 px-4">
                <Checkbox
                  :model-value="allChecked"
                  aria-label="全选本页"
                  @update:model-value="(v) => store.toggleSelectAll(!!v)"
                />
              </TableHead>
              <TableHead class="h-11 min-w-[16rem] px-2">节点</TableHead>
              <TableHead class="h-11 w-40 px-3">热键</TableHead>
              <TableHead class="h-11 w-28 px-3">标记</TableHead>
              <TableHead class="h-11 w-36 px-4 text-right">操作</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow
              v-for="node in store.nodes"
              :key="node.nodeId"
              class="group cursor-grab border-border/70 hover:bg-muted/40 active:cursor-grabbing"
              draggable="true"
              :data-state="store.selectedNodeId === node.nodeId || store.draft?.nodeId === node.nodeId ? 'selected' : undefined"
              @click="onRowClick(node)"
              @dragstart="onRowDragStart(node, $event)"
            >
              <TableCell class="px-4 py-3.5" @click.stop>
                <Checkbox
                  :model-value="store.selectedIds.includes(node.nodeId)"
                  @update:model-value="(v) => store.toggleSelect(node.nodeId, !!v)"
                />
              </TableCell>
              <TableCell class="px-2 py-3.5">
                <div class="flex min-w-0 items-center gap-3">
                  <span
                    class="grid size-9 shrink-0 place-items-center rounded-lg border border-border/80 bg-muted/40 text-muted-foreground shadow-sm"
                  >
                    <Keyboard class="size-4" />
                  </span>
                  <div class="min-w-0">
                    <p class="truncate text-sm font-medium leading-snug text-foreground">
                      {{ node.name }}
                    </p>
                    <p class="mt-0.5 truncate text-[11px] text-muted-foreground">
                      {{ node.description || '无描述' }}
                    </p>
                  </div>
                </div>
              </TableCell>
              <TableCell class="px-3 py-3.5">
                <span
                  v-if="node.hotkeyDisplay"
                  class="inline-flex max-w-full truncate rounded-md bg-muted/60 px-2 py-1 font-mono text-[11px] text-muted-foreground ring-1 ring-border/60"
                >
                  {{ node.hotkeyDisplay }}
                </span>
                <span v-else class="text-xs text-muted-foreground">—</span>
              </TableCell>
              <TableCell class="px-3 py-3.5">
                <span
                  v-if="node.isBuiltIn"
                  class="inline-flex rounded px-1.5 py-0.5 text-[11px] text-muted-foreground ring-1 ring-border/70"
                >
                  内置 · 只读
                </span>
                <span v-else class="text-xs text-muted-foreground">自定义</span>
              </TableCell>
              <TableCell class="px-4 py-3.5" @click.stop>
                <div
                  class="flex items-center justify-end gap-0.5 opacity-70 transition-opacity group-hover:opacity-100"
                >
                  <Button
                    size="icon"
                    variant="ghost"
                    class="size-8"
                    @click="onEdit(node, $event)"
                  >
                    <Pencil class="size-3.5" />
                  </Button>
                  <Button
                    size="icon"
                    variant="ghost"
                    class="size-8"
                    @click="onCopy(node, $event)"
                  >
                    <Copy class="size-3.5" />
                  </Button>
                  <Button
                    size="icon"
                    variant="ghost"
                    class="size-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
                    :disabled="!node.canDelete || node.isBuiltIn"
                    @click="onDelete(node, $event)"
                  >
                    <Trash2 class="size-3.5" />
                  </Button>
                </div>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>

      <div class="ks-shell-footer flex items-center justify-between gap-3 px-4 py-2.5">
        <span class="text-xs text-muted-foreground">
          共 <span class="tabular-nums text-foreground">{{ store.total }}</span> 项
          <span v-if="store.selectedIds.length" class="ml-2">
            · 已选
            <span class="tabular-nums text-foreground">{{ store.selectedIds.length }}</span>
          </span>
        </span>
        <div class="flex items-center gap-1.5">
          <Button
            variant="outline"
            size="icon"
            class="size-8"
            :disabled="store.page <= 1"
            @click="store.setPage(store.page - 1)"
          >
            <ChevronLeft class="size-4" />
          </Button>
          <span class="min-w-16 text-center text-xs tabular-nums text-muted-foreground">
            {{ store.page }} / {{ pageCount }}
          </span>
          <Button
            variant="outline"
            size="icon"
            class="size-8"
            :disabled="store.page >= pageCount"
            @click="store.setPage(store.page + 1)"
          >
            <ChevronRight class="size-4" />
          </Button>
        </div>
      </div>
    </template>
  </div>
</template>
