<script setup>
import { computed } from 'vue';
import { Button } from '@/components/ui/button';
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
  get: () => store.dialogs.deleteCategoryConfirm,
  set: (v) => {
    store.dialogs.deleteCategoryConfirm = v;
    if (!v) store.pendingDeleteCategoryId = null;
  },
});

const category = computed(() =>
  store.categories.find((c) => c.categoryId === store.pendingDeleteCategoryId) ?? null);
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>删除分类？</DialogTitle>
        <DialogDescription>
          将删除分类「{{ category?.name || '未命名' }}」及其下全部
          {{ category?.nodeCount ?? 0 }} 个节点，此操作不可撤销。
        </DialogDescription>
      </DialogHeader>
      <DialogFooter>
        <Button variant="outline" @click="open = false">取消</Button>
        <Button variant="destructive" @click="store.confirmDeleteCategory()">
          删除分类与节点
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
