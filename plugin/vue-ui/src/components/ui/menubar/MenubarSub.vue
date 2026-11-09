<script setup>
import { MenubarSub, useForwardPropsEmits } from "reka-ui";

const props = defineProps({
  defaultOpen: { type: Boolean, required: false },
  open: { type: Boolean, required: false },
});
const emits = defineEmits(["update:open"]);

const forwarded = useForwardPropsEmits(props, emits);
</script>

<template>
  <MenubarSub data-slot="menubar-sub" v-bind="forwarded">
    <slot />
  </MenubarSub>
</template>
