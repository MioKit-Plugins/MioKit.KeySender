<script setup>
import { computed } from 'vue';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { keyDisplay } from '@/api/types.js';
import { useManagerStore } from '@/stores/useManagerStore';

const props = defineProps({
  modelValue: { type: String, default: '' },
  disabled: { type: Boolean, default: false },
});

const emit = defineEmits(['update:modelValue']);
const store = useManagerStore();

const groups = computed(() => {
  const map = new Map();
  for (const item of store.keyCatalog) {
    const g = item.group || '其它';
    if (!map.has(g)) map.set(g, []);
    map.get(g).push(item);
  }
  return [...map.entries()];
});

const label = computed(() =>
  props.modelValue ? keyDisplay(props.modelValue) || props.modelValue : '选择按键');
</script>

<template>
  <Select
    :model-value="modelValue || undefined"
    :disabled="disabled"
    @update:model-value="(v) => emit('update:modelValue', v)"
  >
    <SelectTrigger class="h-8 w-full min-w-[7.5rem]" :aria-label="label">
      <SelectValue :placeholder="'选择按键'" />
    </SelectTrigger>
    <SelectContent class="max-h-72">
      <SelectGroup v-for="[group, items] in groups" :key="group">
        <SelectLabel>{{ group }}</SelectLabel>
        <SelectItem v-for="item in items" :key="item.key" :value="item.key">
          {{ item.display }}
        </SelectItem>
      </SelectGroup>
    </SelectContent>
  </Select>
</template>
