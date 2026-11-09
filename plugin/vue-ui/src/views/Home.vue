<script setup>
import { ref, h } from 'vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Checkbox } from '@/components/ui/checkbox';
import { Switch } from '@/components/ui/switch';
import { Slider } from '@/components/ui/slider';
import { Progress } from '@/components/ui/progress';
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '@/components/ui/tabs';
import { DataTable } from '@/components/ui/data-table';
import { Badge } from '@/components/ui/badge';
import { ActionIcon } from '@/components/ui/action-icon';
import { Pencil, Trash2, Sun, Moon } from 'lucide-vue-next';
import { useThemeStore } from '@/store/useThemeStore';
import { toast } from 'vue-sonner';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { ConfirmPopover } from '@/components/ui/confirm-popover';

const themeStore = useThemeStore();

// 状态
const inputValue = ref('');
const checkboxValue = ref(false);
const switchValue = ref(false);
const sliderValue = ref([50]);
const radioValue = ref('option1');
const selectValue = ref('');
const selectedRows = ref([]);

// Dialog 状态
const editDialogOpen = ref(false);
const editingRow = ref(null);
const editForm = ref({
  name: '',
  email: '',
  role: '',
  department: '',
});

function openEditDialog(row) {
  editingRow.value = row;
  editForm.value = {
    name: row.name,
    email: row.email,
    role: row.role,
    department: row.department,
  };
  editDialogOpen.value = true;
}

function handleSave() {
  // 这里可以调用 API 保存
  toast.success('保存成功', { description: `用户 ${editForm.value.name} 已更新` });
  editDialogOpen.value = false;
}

function handleDelete(row) {
  toast.error('删除成功', { description: `用户 ${row.name} 已删除` });
}

function showLoading() {
  window.loading.show('加载中...');
  setTimeout(() => {
    window.loading.hide();
  }, 2000);
}

// 表格列定义
const tableColumns = [
  {
    accessorKey: 'id',
    header: 'ID',
    size: 60,
  },
  {
    accessorKey: 'name',
    header: '名称',
    size: 120,
  },
  {
    accessorKey: 'email',
    header: '邮箱',
    size: 200,
  },
  {
    accessorKey: 'status',
    header: '状态',
    size: 100,
    cell: ({ row }) => {
      const status = row.getValue('status');
      const variant = status === 'active' ? 'default' : status === 'pending' ? 'secondary' : 'outline';
      const label = status === 'active' ? '活跃' : status === 'pending' ? '待处理' : '已禁用';
      return h(Badge, { variant }, () => label);
    },
  },
  {
    accessorKey: 'role',
    header: '角色',
    size: 100,
  },
  {
    accessorKey: 'department',
    header: '部门',
    size: 120,
  },
  {
    accessorKey: 'joinDate',
    header: '加入日期',
    size: 120,
  },
  {
    accessorKey: 'salary',
    header: '薪资',
    size: 100,
    cell: ({ row }) => `¥${row.getValue('salary').toLocaleString()}`,
  },
  {
    id: 'actions',
    header: '操作',
    size: 80,
    enableHiding: false,
    cell: ({ row }) => {
      return h('div', { class: 'flex gap-1' }, [
        h(ActionIcon, { variant: 'primary', onClick: () => openEditDialog(row.original) }, () => h(Pencil)),
        h(ConfirmPopover, {
          title: '删除确认',
          description: `是否确认删除用户 ${row.original.name}？`,
          onConfirm: () => handleDelete(row.original),
        }, () => h(ActionIcon, { variant: 'error' }, () => h(Trash2))),
      ]);
    },
  },
];

// 生成测试数据
const departments = ['技术部', '产品部', '设计部', '市场部', '运营部', '财务部'];
const roles = ['工程师', '经理', '设计师', '分析师', '专员'];
const statuses = ['active', 'pending', 'disabled'];

const tableData = Array.from({ length: 267 }, (_, i) => ({
  id: i + 1,
  name: `用户${i + 1}`,
  email: `user${i + 1}@example.com`,
  status: statuses[Math.floor(Math.random() * statuses.length)],
  role: roles[Math.floor(Math.random() * roles.length)],
  department: departments[Math.floor(Math.random() * departments.length)],
  joinDate: `2024-${String(Math.floor(Math.random() * 12) + 1).padStart(2, '0')}-${String(Math.floor(Math.random() * 28) + 1).padStart(2, '0')}`,
  salary: Math.floor(Math.random() * 30000) + 10000,
}));

function handleSelectionChange(rows) {
  selectedRows.value = rows;
}
</script>

<template>
  <div class="space-y-8 p-6">
    <div class="flex items-center justify-between">
      <h1 class="text-2xl font-bold text-foreground">UI Components</h1>
      <Button variant="outline" @click="themeStore.toggleTheme">
        <Sun v-if="themeStore.theme === 'dark'" class="size-4" />
        <Moon v-else class="size-4" />
        {{ themeStore.theme === 'dark' ? '浅色模式' : '深色模式' }}
      </Button>
    </div>

    <!-- Buttons -->
    <Card>
      <CardHeader>
        <CardTitle>Button</CardTitle>
        <CardDescription>按钮变体</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-wrap gap-3">
        <Button>Default</Button>
        <Button variant="secondary">Secondary</Button>
        <Button variant="outline">Outline</Button>
        <Button variant="ghost">Ghost</Button>
        <Button variant="destructive">Destructive</Button>
        <Button variant="link">Link</Button>
        <Button disabled>Disabled</Button>
      </CardContent>
    </Card>

    <!-- Toast -->
    <Card>
      <CardHeader>
        <CardTitle>Toast & Loading</CardTitle>
        <CardDescription>消息提示和加载状态</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-wrap gap-3">
        <Button variant="outline" @click="toast('默认消息', { description: '这是一条普通消息' })">
          Default
        </Button>
        <Button variant="outline" @click="toast.info('信息', { description: '这是一条信息提示' })">
          Info
        </Button>
        <Button variant="outline" @click="toast.success('成功', { description: '操作已成功完成' })">
          Success
        </Button>
        <Button variant="outline" @click="toast.warning('警告', { description: '请注意这个操作' })">
          Warning
        </Button>
        <Button variant="outline" @click="toast.error('错误', { description: '操作失败，请重试' })">
          Error
        </Button>
        <Button variant="outline" @click="showLoading">
          Loading (2s)
        </Button>
      </CardContent>
    </Card>

    <!-- Input -->
    <Card>
      <CardHeader>
        <CardTitle>Input</CardTitle>
        <CardDescription>输入框</CardDescription>
      </CardHeader>
      <CardContent class="space-y-3 max-w-sm">
        <Input v-model="inputValue" placeholder="请输入内容..." />
        <Input disabled placeholder="Disabled" />
      </CardContent>
    </Card>

    <!-- Checkbox & Switch -->
    <Card>
      <CardHeader>
        <CardTitle>Checkbox & Switch</CardTitle>
        <CardDescription>复选框和开关</CardDescription>
      </CardHeader>
      <CardContent class="space-y-4">
        <div class="flex items-center gap-2">
          <Checkbox id="checkbox" v-model:checked="checkboxValue" />
          <Label for="checkbox">Checkbox: {{ checkboxValue }}</Label>
        </div>
        <div class="flex items-center gap-2">
          <Switch id="switch" v-model:checked="switchValue" />
          <Label for="switch">Switch: {{ switchValue }}</Label>
        </div>
      </CardContent>
    </Card>

    <!-- Radio Group -->
    <Card>
      <CardHeader>
        <CardTitle>Radio Group</CardTitle>
        <CardDescription>单选组</CardDescription>
      </CardHeader>
      <CardContent>
        <RadioGroup v-model="radioValue" class="flex gap-4">
          <div class="flex items-center gap-2">
            <RadioGroupItem id="r1" value="option1" />
            <Label for="r1">Option 1</Label>
          </div>
          <div class="flex items-center gap-2">
            <RadioGroupItem id="r2" value="option2" />
            <Label for="r2">Option 2</Label>
          </div>
          <div class="flex items-center gap-2">
            <RadioGroupItem id="r3" value="option3" />
            <Label for="r3">Option 3</Label>
          </div>
        </RadioGroup>
        <p class="text-sm text-muted-foreground mt-2">Selected: {{ radioValue }}</p>
      </CardContent>
    </Card>

    <!-- Select -->
    <Card>
      <CardHeader>
        <CardTitle>Select</CardTitle>
        <CardDescription>下拉选择</CardDescription>
      </CardHeader>
      <CardContent class="max-w-sm">
        <Select v-model="selectValue">
          <SelectTrigger>
            <SelectValue placeholder="请选择..." />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="apple">Apple</SelectItem>
            <SelectItem value="banana">Banana</SelectItem>
            <SelectItem value="orange">Orange</SelectItem>
          </SelectContent>
        </Select>
      </CardContent>
    </Card>

    <!-- Slider & Progress -->
    <Card>
      <CardHeader>
        <CardTitle>Slider & Progress</CardTitle>
        <CardDescription>滑块和进度条</CardDescription>
      </CardHeader>
      <CardContent class="space-y-4 max-w-sm">
        <div>
          <Label class="mb-2 block">Slider: {{ sliderValue[0] }}</Label>
          <Slider v-model="sliderValue" :max="100" :step="1" />
        </div>
        <div>
          <Label class="mb-2 block">Progress: {{ sliderValue[0] }}%</Label>
          <Progress :model-value="sliderValue[0]" />
        </div>
      </CardContent>
    </Card>

    <!-- Tabs -->
    <Card>
      <CardHeader>
        <CardTitle>Tabs</CardTitle>
        <CardDescription>选项卡</CardDescription>
      </CardHeader>
      <CardContent>
        <Tabs default-value="tab1">
          <TabsList>
            <TabsTrigger value="tab1">Tab 1</TabsTrigger>
            <TabsTrigger value="tab2">Tab 2</TabsTrigger>
            <TabsTrigger value="tab3">Tab 3</TabsTrigger>
          </TabsList>
          <TabsContent value="tab1" class="p-4">
            <p class="text-muted-foreground">Content for Tab 1</p>
          </TabsContent>
          <TabsContent value="tab2" class="p-4">
            <p class="text-muted-foreground">Content for Tab 2</p>
          </TabsContent>
          <TabsContent value="tab3" class="p-4">
            <p class="text-muted-foreground">Content for Tab 3</p>
          </TabsContent>
        </Tabs>
      </CardContent>
    </Card>

    <!-- Colors -->
    <Card>
      <CardHeader>
        <CardTitle>Theme Colors</CardTitle>
        <CardDescription>主题色板</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="grid grid-cols-4 gap-3">
          <div class="space-y-1">
            <div class="h-10 rounded bg-background border border-border"></div>
            <p class="text-xs text-center">background</p>
          </div>
          <div class="space-y-1">
            <div class="h-10 rounded bg-foreground"></div>
            <p class="text-xs text-center">foreground</p>
          </div>
          <div class="space-y-1">
            <div class="h-10 rounded bg-primary"></div>
            <p class="text-xs text-center">primary</p>
          </div>
          <div class="space-y-1">
            <div class="h-10 rounded bg-secondary"></div>
            <p class="text-xs text-center">secondary</p>
          </div>
          <div class="space-y-1">
            <div class="h-10 rounded bg-muted"></div>
            <p class="text-xs text-center">muted</p>
          </div>
          <div class="space-y-1">
            <div class="h-10 rounded bg-accent"></div>
            <p class="text-xs text-center">accent</p>
          </div>
          <div class="space-y-1">
            <div class="h-10 rounded bg-destructive"></div>
            <p class="text-xs text-center">destructive</p>
          </div>
          <div class="space-y-1">
            <div class="h-10 rounded border border-border bg-card"></div>
            <p class="text-xs text-center">card</p>
          </div>
        </div>
      </CardContent>
    </Card>

    <!-- Data Table -->
    <Card>
      <CardHeader>
        <CardTitle>Data Table</CardTitle>
        <CardDescription>
          数据表格 - 支持多选、固定列、显示/隐藏列、分页
          <span v-if="selectedRows.length" class="ml-2 text-primary">
            (已选 {{ selectedRows.length }} 项)
          </span>
        </CardDescription>
      </CardHeader>
      <CardContent>
        <DataTable
          :columns="tableColumns"
          :data="tableData"
          :sticky-left-columns="['id']"
          :sticky-right-columns="['actions']"
          @selection-change="handleSelectionChange"
        />
      </CardContent>
    </Card>

    <!-- Edit Dialog -->
    <Dialog v-model:open="editDialogOpen">
      <DialogContent class="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>编辑用户</DialogTitle>
          <DialogDescription>
            修改用户 ID: {{ editingRow?.id }} 的信息
          </DialogDescription>
        </DialogHeader>
        <div class="space-y-4 py-4">
          <div class="space-y-2">
            <Label>名称</Label>
            <Input v-model="editForm.name" placeholder="请输入名称" />
          </div>
          <div class="space-y-2">
            <Label>邮箱</Label>
            <Input v-model="editForm.email" placeholder="请输入邮箱" />
          </div>
          <div class="space-y-2">
            <Label>角色</Label>
            <Select v-model="editForm.role">
              <SelectTrigger>
                <SelectValue placeholder="请选择角色" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="工程师">工程师</SelectItem>
                <SelectItem value="经理">经理</SelectItem>
                <SelectItem value="设计师">设计师</SelectItem>
                <SelectItem value="分析师">分析师</SelectItem>
                <SelectItem value="专员">专员</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div class="space-y-2">
            <Label>部门</Label>
            <Select v-model="editForm.department">
              <SelectTrigger>
                <SelectValue placeholder="请选择部门" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="技术部">技术部</SelectItem>
                <SelectItem value="产品部">产品部</SelectItem>
                <SelectItem value="设计部">设计部</SelectItem>
                <SelectItem value="市场部">市场部</SelectItem>
                <SelectItem value="运营部">运营部</SelectItem>
                <SelectItem value="财务部">财务部</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="editDialogOpen = false">取消</Button>
          <Button @click="handleSave">保存</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
