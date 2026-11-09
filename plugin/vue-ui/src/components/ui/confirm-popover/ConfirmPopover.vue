<script setup>
import { ref } from 'vue';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { Button } from '@/components/ui/button';
import { TriangleAlert } from 'lucide-vue-next';

const props = defineProps({
  title: { type: String, default: '删除确认' },
  description: { type: String, default: '是否确认删除选择项目？' },
  confirmText: { type: String, default: '确定' },
  cancelText: { type: String, default: '取消' },
  side: { type: String, default: 'bottom' },
  align: { type: String, default: 'center' },
});

const emit = defineEmits(['confirm', 'cancel']);

const open = ref(false);

function handleConfirm() {
  emit('confirm');
  open.value = false;
}

function handleCancel() {
  emit('cancel');
  open.value = false;
}
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <slot />
    </PopoverTrigger>
    <PopoverContent 
      :side="side" 
      :align="align" 
      class="w-72 p-4"
      :side-offset="8"
    >
      <div class="flex gap-3">
        <TriangleAlert class="size-6 text-amber-500 shrink-0 mt-0.5" />
        <div class="flex-1">
          <div class="font-medium text-foreground">{{ title }}</div>
          <div class="text-sm text-muted-foreground mt-1">{{ description }}</div>
        </div>
      </div>
      <div class="flex justify-end gap-2 mt-4">
        <Button variant="outline" size="sm" @click="handleCancel">
          {{ cancelText }}
        </Button>
        <Button size="sm" @click="handleConfirm">
          {{ confirmText }}
        </Button>
      </div>
    </PopoverContent>
  </Popover>
</template>
