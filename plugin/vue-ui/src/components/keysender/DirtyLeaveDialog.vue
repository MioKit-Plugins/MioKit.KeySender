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

const open = computed({
  get: () => store.dialogs.dirtyLeave,
  set: (v) => {
    if (!v) store.dirtyLeaveStay();
  },
});
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>有未保存的修改</DialogTitle>
        <DialogDescription>
          离开当前草稿前请选择：保存并继续、放弃修改，或继续编辑。
        </DialogDescription>
      </DialogHeader>
      <DialogFooter class="flex-col gap-2 sm:flex-row sm:justify-end">
        <Button
          class="min-w-28"
          :disabled="store.saving"
          @click="store.dirtyLeaveSave()"
        >
          保存并继续
        </Button>
        <Button variant="secondary" class="min-w-28" :disabled="store.saving" @click="store.dirtyLeaveDiscard()">
          放弃修改
        </Button>
        <Button variant="outline" class="min-w-28" @click="store.dirtyLeaveStay()">
          继续编辑
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
