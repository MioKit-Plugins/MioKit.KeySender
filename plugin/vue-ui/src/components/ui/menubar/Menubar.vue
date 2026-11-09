<script setup>
import { reactiveOmit } from "@vueuse/core";
import { MenubarRoot, useForwardPropsEmits } from "reka-ui";
import { cn } from "@/lib/utils";

const props = defineProps({
  modelValue: { type: String, required: false },
  defaultValue: { type: String, required: false },
  dir: { type: String, required: false },
  loop: { type: Boolean, required: false },
  class: { type: null, required: false },
});
const emits = defineEmits(["update:modelValue"]);

const delegatedProps = reactiveOmit(props, "class");

const forwarded = useForwardPropsEmits(delegatedProps, emits);
</script>

<template>
  <MenubarRoot
    data-slot="menubar"
    v-bind="forwarded"
    :class="
      cn(
        'bg-background flex h-9 items-center gap-1 rounded-md border p-1 shadow-xs',
        props.class,
      )
    "
  >
    <slot />
  </MenubarRoot>
</template>
