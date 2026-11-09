<script setup>
import { ref } from 'vue';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { newEventId, keyDisplay } from '@/api/types.js';
import KeyPicker from './KeyPicker.vue';

const emit = defineEmits(['insert', 'cancel']);

const pendingKey = ref('LeftControl');
const keys = ref([]);

function addKey() {
  if (!pendingKey.value) return;
  keys.value.push(pendingKey.value);
}

function removeAt(index) {
  keys.value.splice(index, 1);
}

function generate() {
  if (!keys.value.length) return;
  const downs = keys.value.map((key) => ({
    eventId: newEventId(),
    type: 'keyDown',
    key,
    delayBeforeMs: 0,
  }));
  const ups = [...keys.value].reverse().map((key) => ({
    eventId: newEventId(),
    type: 'keyUp',
    key,
    delayBeforeMs: 0,
  }));
  emit('insert', [...downs, ...ups]);
  keys.value = [];
}
</script>

<template>
  <div class="flex flex-wrap items-center gap-2 py-1">
    <KeyPicker v-model="pendingKey" />
    <Button size="sm" variant="secondary" class="min-w-16" @click="addKey">添加键</Button>
    <div class="flex min-h-6 flex-wrap items-center gap-1">
      <Badge
        v-for="(k, i) in keys"
        :key="`${k}-${i}`"
        variant="outline"
        class="cursor-pointer"
        @click="removeAt(i)"
      >
        {{ keyDisplay(k) }}
      </Badge>
      <span v-if="!keys.length" class="text-xs text-muted-foreground">按顺序添加修饰键与主键</span>
    </div>
    <Button size="sm" class="min-w-20" :disabled="!keys.length" @click="generate">生成</Button>
    <Button size="sm" variant="ghost" class="min-w-16" @click="emit('cancel')">取消</Button>
  </div>
</template>
