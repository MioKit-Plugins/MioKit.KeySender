<script setup>
import { computed } from 'vue';
import { cn } from '@/lib/utils';

const props = defineProps({
  variant: {
    type: String,
    default: 'default',
    validator: (v) => ['default', 'primary', 'success', 'warning', 'error'].includes(v),
  },
  class: { type: null, required: false },
});

const emit = defineEmits(['click']);

const variantClasses = {
  default: 'text-muted-foreground hover:text-foreground',
  primary: 'text-muted-foreground hover:text-primary',
  success: 'text-muted-foreground hover:text-green-500',
  warning: 'text-muted-foreground hover:text-amber-500',
  error: 'text-muted-foreground hover:text-destructive',
};

const classes = computed(() =>
  cn(
    'inline-flex items-center justify-center size-7 rounded cursor-pointer',
    'transition-colors duration-200',
    '[&_svg]:size-4 [&_svg]:shrink-0 [&_svg]:transition-transform [&_svg]:duration-200',
    '[&:hover_svg]:scale-110',
    variantClasses[props.variant],
    props.class
  )
);
</script>

<template>
  <span :class="classes" @click="emit('click', $event)">
    <slot />
  </span>
</template>
