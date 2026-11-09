<script setup>
import { computed } from 'vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { useManagerStore } from '@/stores/useManagerStore';

const store = useManagerStore();

const open = computed({
  get: () => store.categoryDialog.open,
  set: (v) => {
    if (!v) store.closeCategoryDialog();
  },
});

const title = computed(() =>
  (store.categoryDialog.mode === 'create' ? '新建分类' : '重命名分类'));
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>{{ title }}</DialogTitle>
        <DialogDescription>
          分类用于组织按键序列；删除分类会同时删除其中全部节点。
        </DialogDescription>
      </DialogHeader>

      <div class="space-y-3 py-1">
        <div class="space-y-1.5">
          <Label for="ks-category-name">名称</Label>
          <Input
            id="ks-category-name"
            :model-value="store.categoryDialog.name"
            maxlength="100"
            placeholder="例如：办公"
            @update:model-value="(v) => { store.categoryDialog.name = String(v ?? ''); }"
            @keydown.enter="store.submitCategoryDialog()"
          />
        </div>
        <div class="space-y-1.5">
          <Label for="ks-category-desc">说明</Label>
          <Input
            id="ks-category-desc"
            :model-value="store.categoryDialog.description"
            maxlength="500"
            placeholder="可选"
            @update:model-value="(v) => { store.categoryDialog.description = String(v ?? ''); }"
          />
        </div>
      </div>

      <DialogFooter>
        <Button variant="outline" @click="store.closeCategoryDialog()">取消</Button>
        <Button @click="store.submitCategoryDialog()">保存</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
