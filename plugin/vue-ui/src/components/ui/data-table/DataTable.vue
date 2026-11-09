<script setup>
import { ref, computed, watch, h } from 'vue';
import {
  FlexRender,
  getCoreRowModel,
  getPaginationRowModel,
  useVueTable,
} from '@tanstack/vue-table';
import { useVirtualizer } from '@tanstack/vue-virtual';
import { cn } from '@/lib/utils';
import { Checkbox } from '@/components/ui/checkbox';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Button } from '@/components/ui/button';
import { Settings2, ChevronLeft, ChevronRight, MoreHorizontal, Check } from 'lucide-vue-next';

const props = defineProps({
  columns: { type: Array, required: true },
  data: { type: Array, required: true },
  enableSelection: { type: Boolean, default: true },
  enableColumnVisibility: { type: Boolean, default: true },
  enablePagination: { type: Boolean, default: true },
  enableVirtualization: { type: Boolean, default: false },
  virtualHeight: { type: Number, default: 400 },
  rowHeight: { type: Number, default: 40 },
  pageSizeOptions: { type: Array, default: () => [10, 20, 50] },
  stickyLeftColumns: { type: Array, default: () => [] },
  stickyRightColumns: { type: Array, default: () => [] },
});

const emit = defineEmits(['selectionChange']);

const rowSelection = ref({});
const columnVisibility = ref({});
const pagination = ref({ pageIndex: 0, pageSize: props.pageSizeOptions[0] });

// 构建带选择列的 columns
const tableColumns = computed(() => {
  const cols = [...props.columns];
  if (props.enableSelection) {
    cols.unshift({
      id: 'select',
      header: ({ table }) => h(Checkbox, {
        'model-value': table.getIsAllPageRowsSelected() || (table.getIsSomePageRowsSelected() && 'indeterminate'),
        'onUpdate:modelValue': (value) => table.toggleAllPageRowsSelected(!!value),
        ariaLabel: 'Select all',
      }),
      cell: ({ row }) => h(Checkbox, {
        'model-value': row.getIsSelected(),
        'onUpdate:modelValue': (value) => row.toggleSelected(!!value),
        ariaLabel: 'Select row',
      }),
      enableSorting: false,
      enableHiding: false,
      size: 40,
    });
  }
  return cols;
});

const table = useVueTable({
  get data() { return props.data; },
  get columns() { return tableColumns.value; },
  getCoreRowModel: getCoreRowModel(),
  getPaginationRowModel: getPaginationRowModel(),
  onRowSelectionChange: (updaterOrValue) => {
    rowSelection.value = typeof updaterOrValue === 'function'
      ? updaterOrValue(rowSelection.value)
      : updaterOrValue;
  },
  onColumnVisibilityChange: (updaterOrValue) => {
    columnVisibility.value = typeof updaterOrValue === 'function'
      ? updaterOrValue(columnVisibility.value)
      : updaterOrValue;
  },
  onPaginationChange: (updaterOrValue) => {
    pagination.value = typeof updaterOrValue === 'function'
      ? updaterOrValue(pagination.value)
      : updaterOrValue;
  },
  state: {
    get rowSelection() { return rowSelection.value; },
    get columnVisibility() { return columnVisibility.value; },
    get pagination() { return pagination.value; },
  },
});

// 虚拟化
const tableContainerRef = ref(null);
const rows = computed(() => table.getRowModel().rows);

const virtualizer = computed(() => {
  if (!props.enableVirtualization) return null;
  return useVirtualizer({
    count: rows.value.length,
    getScrollElement: () => tableContainerRef.value,
    estimateSize: () => props.rowHeight,
    overscan: 10,
  });
});

const virtualRows = computed(() => virtualizer.value?.getVirtualItems() ?? []);
const totalSize = computed(() => virtualizer.value?.getTotalSize() ?? 0);

// 选中行变化时触发事件
watch(rowSelection, () => {
  const selectedRows = table.getFilteredSelectedRowModel().rows.map(row => row.original);
  emit('selectionChange', selectedRows);
}, { deep: true });

// 分页信息
const totalRows = computed(() => props.data.length);
const currentPage = computed(() => pagination.value.pageIndex + 1);
const totalPages = computed(() => table.getPageCount());
const pageSize = computed({
  get: () => String(pagination.value.pageSize),
  set: (val) => table.setPageSize(Number(val)),
});

// 按照 Avalonia 逻辑生成页码按钮（固定7个位置）
const pageButtons = computed(() => {
  const pageCount = totalPages.value;
  const current = currentPage.value;
  const buttons = [];

  if (pageCount <= 7) {
    for (let i = 1; i <= pageCount; i++) {
      buttons.push({ page: i, isEllipsis: false });
    }
  } else {
    const clamp = (val, min, max) => Math.max(min, Math.min(max, val));
    const mid = clamp(current, 4, pageCount - 3);

    // buttons[0] = 1
    buttons.push({ page: 1, isEllipsis: false });
    // buttons[1] = 省略号(向左跳5页)或 mid-2
    if (mid > 4) {
      buttons.push({ page: -1, isEllipsis: true, jumpTo: Math.max(1, current - 5) });
    } else {
      buttons.push({ page: mid - 2, isEllipsis: false });
    }
    // buttons[2] = mid-1
    buttons.push({ page: mid - 1, isEllipsis: false });
    // buttons[3] = mid
    buttons.push({ page: mid, isEllipsis: false });
    // buttons[4] = mid+1
    buttons.push({ page: mid + 1, isEllipsis: false });
    // buttons[5] = 省略号(向右跳5页)或 mid+2
    if (mid < pageCount - 3) {
      buttons.push({ page: -1, isEllipsis: true, jumpTo: Math.min(pageCount, current + 5) });
    } else {
      buttons.push({ page: mid + 2, isEllipsis: false });
    }
    // buttons[6] = pageCount
    buttons.push({ page: pageCount, isEllipsis: false });
  }

  return buttons;
});

// 可隐藏的列（排除 select 和操作列）
const hideableColumns = computed(() => {
  return table.getAllColumns().filter(col => {
    const canHide = col.getCanHide();
    const id = col.id;
    return canHide && id !== 'select' && id !== 'actions';
  });
});

// 固定列样式
function getStickyStyle(columnId, isHeader = false) {
  const visibleCols = table.getVisibleLeafColumns();
  const isLeftSticky = props.stickyLeftColumns.includes(columnId) || columnId === 'select';
  const isRightSticky = props.stickyRightColumns.includes(columnId);

  if (!isLeftSticky && !isRightSticky) return {};

  if (isLeftSticky) {
    let left = 0;
    for (const col of visibleCols) {
      if (col.id === columnId) break;
      if (props.stickyLeftColumns.includes(col.id) || col.id === 'select') {
        left += col.getSize();
      }
    }
    return {
      position: 'sticky',
      left: `${left}px`,
      zIndex: isHeader ? 30 : 20,
      backgroundColor: 'var(--background)',
    };
  }

  if (isRightSticky) {
    let right = 0;
    for (let i = visibleCols.length - 1; i >= 0; i--) {
      const col = visibleCols[i];
      if (col.id === columnId) break;
      if (props.stickyRightColumns.includes(col.id)) {
        right += col.getSize();
      }
    }
    return {
      position: 'sticky',
      right: `${right}px`,
      zIndex: isHeader ? 30 : 20,
      backgroundColor: 'var(--background)',
    };
  }

  return {};
}

function toggleColumnVisibility(column) {
  column.toggleVisibility(!column.getIsVisible());
}

function handlePageButtonClick(btn) {
  if (btn.isEllipsis) {
    table.setPageIndex(btn.jumpTo - 1);
  } else {
    table.setPageIndex(btn.page - 1);
  }
}
</script>

<template>
  <div class="space-y-3">
    <!-- 工具栏 -->
    <div v-if="enableColumnVisibility" class="flex justify-end">
      <DropdownMenu>
        <DropdownMenuTrigger as-child>
          <Button variant="outline" size="sm">
            <Settings2 class="size-4 mr-2" />
            列设置
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" class="w-40">
          <div
            v-for="column in hideableColumns"
            :key="column.id"
            class="flex items-center gap-2 px-2 py-1.5 text-sm cursor-pointer rounded-sm hover:bg-accent"
            @click="toggleColumnVisibility(column)"
          >
            <div class="size-4 flex items-center justify-center border rounded">
              <Check v-if="column.getIsVisible()" class="size-3" />
            </div>
            <span>{{ typeof column.columnDef.header === 'string' ? column.columnDef.header : column.id }}</span>
          </div>
        </DropdownMenuContent>
      </DropdownMenu>
    </div>

    <!-- 表格 -->
    <div
      ref="tableContainerRef"
      :class="cn('rounded-md border overflow-auto')"
      :style="enableVirtualization ? { height: `${virtualHeight}px` } : {}"
    >
      <Table>
        <TableHeader class="sticky top-0 z-20 bg-background">
          <TableRow v-for="headerGroup in table.getHeaderGroups()" :key="headerGroup.id">
            <TableHead
              v-for="header in headerGroup.headers"
              :key="header.id"
              :style="{
                width: `${header.getSize()}px`,
                minWidth: `${header.getSize()}px`,
                ...getStickyStyle(header.column.id, true),
              }"
            >
              <FlexRender
                v-if="!header.isPlaceholder"
                :render="header.column.columnDef.header"
                :props="header.getContext()"
              />
            </TableHead>
          </TableRow>
        </TableHeader>

        <TableBody>
          <!-- 虚拟化模式 -->
          <template v-if="enableVirtualization && virtualizer">
            <tr :style="{ height: `${virtualRows[0]?.start ?? 0}px` }" />
            <TableRow
              v-for="virtualRow in virtualRows"
              :key="rows[virtualRow.index].id"
              :data-state="rows[virtualRow.index].getIsSelected() ? 'selected' : undefined"
              :style="{ height: `${rowHeight}px` }"
            >
              <TableCell
                v-for="cell in rows[virtualRow.index].getVisibleCells()"
                :key="cell.id"
                :style="getStickyStyle(cell.column.id)"
              >
                <FlexRender :render="cell.column.columnDef.cell" :props="cell.getContext()" />
              </TableCell>
            </TableRow>
            <tr :style="{ height: `${totalSize - (virtualRows[virtualRows.length - 1]?.end ?? 0)}px` }" />
          </template>

          <!-- 普通模式 -->
          <template v-else>
            <template v-if="rows.length">
              <TableRow
                v-for="row in rows"
                :key="row.id"
                :data-state="row.getIsSelected() ? 'selected' : undefined"
              >
                <TableCell
                  v-for="cell in row.getVisibleCells()"
                  :key="cell.id"
                  :style="getStickyStyle(cell.column.id)"
                >
                  <FlexRender :render="cell.column.columnDef.cell" :props="cell.getContext()" />
                </TableCell>
              </TableRow>
            </template>
            <TableRow v-else>
              <TableCell :colspan="tableColumns.length" class="h-24 text-center text-muted-foreground">
                暂无数据
              </TableCell>
            </TableRow>
          </template>
        </TableBody>
      </Table>
    </div>

    <!-- 分页 -->
    <div v-if="enablePagination" class="flex items-center justify-between text-sm">
      <div class="flex items-center gap-2 text-muted-foreground">
        <span>共 {{ totalRows }} 条记录</span>
        <Select v-model="pageSize">
          <SelectTrigger class="h-8 w-32">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="size in pageSizeOptions" :key="size" :value="String(size)">
              {{ size }} 条记录
            </SelectItem>
          </SelectContent>
        </Select>
      </div>

      <div class="flex items-center gap-1">
        <Button
          variant="ghost"
          size="icon-sm"
          class="cursor-pointer disabled:cursor-not-allowed"
          :disabled="!table.getCanPreviousPage()"
          @click="table.previousPage()"
        >
          <ChevronLeft class="size-4" />
        </Button>

        <template v-for="(btn, idx) in pageButtons" :key="idx">
          <Button
            v-if="btn.isEllipsis"
            variant="ghost"
            size="icon-sm"
            class="cursor-pointer"
            @click="handlePageButtonClick(btn)"
          >
            <MoreHorizontal class="size-4" />
          </Button>
          <Button
            v-else
            :variant="btn.page === currentPage ? 'default' : 'ghost'"
            size="icon-sm"
            class="cursor-pointer"
            @click="handlePageButtonClick(btn)"
          >
            {{ btn.page }}
          </Button>
        </template>

        <Button
          variant="ghost"
          size="icon-sm"
          class="cursor-pointer disabled:cursor-not-allowed"
          :disabled="!table.getCanNextPage()"
          @click="table.nextPage()"
        >
          <ChevronRight class="size-4" />
        </Button>
      </div>
    </div>
  </div>
</template>
