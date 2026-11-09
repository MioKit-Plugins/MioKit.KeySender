<script setup>
import { ref, onMounted, onUnmounted } from 'vue';
import { Spinner } from '@/components/ui/spinner';

const visible = ref(false);
const message = ref('');

function show(msg = '') {
  message.value = msg;
  visible.value = true;
}

function hide() {
  visible.value = false;
  message.value = '';
}

function setMessage(msg) {
  message.value = msg;
}

// 挂载到 window 供 C# 调用
onMounted(() => {
  window.loading = {
    show,
    hide,
    setMessage,
  };
});

onUnmounted(() => {
  delete window.loading;
});

// 暴露方法供父组件调用
defineExpose({ show, hide, setMessage });
</script>

<template>
  <Teleport to="body">
    <Transition name="loading-fade">
      <div
        v-if="visible"
        class="fixed inset-0 z-[100] flex items-center justify-center bg-black/50"
      >
        <div class="flex flex-col items-center gap-3 rounded-lg bg-background p-6 shadow-lg">
          <Spinner class="size-8 text-primary" />
          <span v-if="message" class="text-sm text-muted-foreground">{{ message }}</span>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.loading-fade-enter-active,
.loading-fade-leave-active {
  transition: opacity 0.2s ease;
}

.loading-fade-enter-from,
.loading-fade-leave-to {
  opacity: 0;
}
</style>
