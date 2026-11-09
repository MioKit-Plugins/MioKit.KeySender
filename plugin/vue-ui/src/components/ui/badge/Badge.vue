<script setup>
import { computed } from 'vue';
import { cn } from '@/lib/utils';

const props = defineProps({
  variant: {
    type: String,
    default: 'default',
    validator: (v) => ['default', 'secondary', 'outline', 'destructive'].includes(v),
  },
  class: { type: null, required: false },
});

const variantClasses = {
  default: 'bg-primary text-primary-foreground',
  secondary: 'bg-secondary text-secondary-foreground',
  outline: 'border border-border text-foreground bg-transparent',
  destructive: 'bg-destructive text-white',
};

const classes = computed(() =>
  cn(
    'inline-flex items-center rounded-md px-2 py-0.5 text-xs font-medium',
    variantClasses[props.variant],
    props.class
  )
);
</script>

<template>
  <span :class="classes">
    <slot />
  </span>
</template>
