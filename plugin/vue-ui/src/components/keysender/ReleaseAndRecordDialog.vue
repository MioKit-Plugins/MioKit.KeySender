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
  get: () => store.dialogs.releaseAndRecord,
  set: (v) => {
    if (!v) store.cancelReleaseAndRecord();
  },
});
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>无法开始录制</DialogTitle>
        <DialogDescription>
          当前仍有模拟按住的键或未结束任务。释放后才能开始录制。
        </DialogDescription>
      </DialogHeader>
      <DialogFooter class="gap-2">
        <Button variant="outline" class="min-w-20" @click="store.cancelReleaseAndRecord()">取消</Button>
        <Button class="min-w-32" @click="store.confirmReleaseAndRecord()">
          释放全部并开始
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
