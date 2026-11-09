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
import { useManagerStore } from '@/stores/useManagerStore';

const store = useManagerStore();

const deleteOpen = computed({
  get: () => store.dialogs.deleteConfirm,
  set: (v) => {
    store.dialogs.deleteConfirm = v;
  },
});

const heldOpen = computed({
  get: () => store.dialogs.heldDelete,
  set: (v) => {
    store.dialogs.heldDelete = v;
  },
});

const count = computed(() => store.pendingDeleteIds.length);
</script>

<template>
  <Dialog v-model:open="deleteOpen">
    <DialogContent class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>删除节点</DialogTitle>
        <DialogDescription>
          将删除 {{ count }} 个节点。外部入口绑定可能失效。此操作不可恢复。
        </DialogDescription>
      </DialogHeader>
      <DialogFooter class="gap-2">
        <Button variant="outline" class="min-w-20" @click="deleteOpen = false">取消</Button>
        <Button
          variant="destructive"
          class="min-w-24"
          @click="store.confirmDelete({ releaseHeldKeys: false })"
        >
          确认删除
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>

  <Dialog v-model:open="heldOpen">
    <DialogContent class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>删除前需释放模拟键</DialogTitle>
        <DialogDescription>
          将取消全部任务并释放仍按下的模拟键，再删除所选节点。
        </DialogDescription>
      </DialogHeader>
      <DialogFooter class="gap-2">
        <Button variant="outline" class="min-w-20" @click="heldOpen = false">取消</Button>
        <Button
          variant="destructive"
          class="min-w-36"
          @click="store.confirmDelete({ releaseHeldKeys: true })"
        >
          释放并删除
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
