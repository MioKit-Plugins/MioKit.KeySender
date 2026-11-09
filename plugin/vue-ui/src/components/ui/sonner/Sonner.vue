<script setup>
import { Toaster as Sonner } from "vue-sonner";

const props = defineProps({
  id: { type: String, required: false },
  invert: { type: Boolean, required: false },
  theme: { type: String, required: false },
  position: { type: String, required: false, default: 'bottom-right' },
  closeButtonPosition: { type: String, required: false },
  hotkey: { type: Array, required: false },
  richColors: { type: Boolean, required: false },
  expand: { type: Boolean, required: false },
  duration: { type: Number, required: false },
  gap: { type: Number, required: false, default: 8 },
  visibleToasts: { type: Number, required: false },
  closeButton: { type: Boolean, required: false, default: true },
  toastOptions: { type: Object, required: false },
  class: { type: String, required: false },
  style: { type: null, required: false },
  offset: { type: [Object, String, Number], required: false, default: 16 },
  mobileOffset: { type: [Object, String, Number], required: false },
  dir: { type: String, required: false },
  swipeDirections: { type: Array, required: false },
  icons: { type: Object, required: false },
  containerAriaLabel: { type: String, required: false },
});
</script>

<template>
  <Sonner
    class="toaster group"
    v-bind="props"
  />
</template>

<style>
/* Toast 基础样式 - 匹配 Avalonia ShadToast */
[data-sonner-toaster] [data-sonner-toast] {
  --width: 340px;
  --border-radius: 8px;
  padding: 10px;
  background: var(--card);
  border: 1px solid var(--border);
  color: var(--foreground);
  font-size: 13px;
}

/* 标题样式 */
[data-sonner-toaster] [data-sonner-toast] [data-title] {
  font-size: 12px;
  font-weight: 600;
  color: var(--foreground);
}

/* 描述样式 */
[data-sonner-toaster] [data-sonner-toast] [data-description] {
  font-size: 13px;
  color: var(--muted-foreground);
}

/* 关闭按钮 */
[data-sonner-toaster] [data-sonner-toast] [data-close-button] {
  position: absolute !important;
  top: 12px !important;
  right: 4px !important;
  left: auto !important;
  background: transparent !important;
  border: none !important;
  color: var(--muted-foreground);
  cursor: pointer;
  padding: 4px;
  border-radius: 4px;
  transition: color 0.2s ease;
}

[data-sonner-toaster] [data-sonner-toast] [data-close-button] svg {
  width: 18px !important;
  height: 18px !important;
  transition: transform 0.2s ease;
}

[data-sonner-toaster] [data-sonner-toast] [data-close-button]:hover {
  color: var(--destructive);
  background: transparent !important;
}

[data-sonner-toaster] [data-sonner-toast] [data-close-button]:hover svg {
  transform: scale(1.15);
}

/* 图标样式 */
[data-sonner-toaster] [data-sonner-toast] [data-icon] {
  width: 24px;
  height: 24px;
  margin-right: 5px;
}

/* Success 样式 - 绿色边框 */
[data-sonner-toaster] [data-sonner-toast][data-type="success"] {
  border-color: #22c55e;
}

[data-sonner-toaster] [data-sonner-toast][data-type="success"] [data-icon] {
  color: #22c55e;
}

/* Error 样式 - 红色边框 */
[data-sonner-toaster] [data-sonner-toast][data-type="error"] {
  border-color: var(--destructive);
}

[data-sonner-toaster] [data-sonner-toast][data-type="error"] [data-icon] {
  color: var(--destructive);
}

/* Warning 样式 - 琥珀色边框 */
[data-sonner-toaster] [data-sonner-toast][data-type="warning"] {
  border-color: #f59e0b;
}

[data-sonner-toaster] [data-sonner-toast][data-type="warning"] [data-icon] {
  color: #f59e0b;
}

/* Info 样式 - 默认边框 */
[data-sonner-toaster] [data-sonner-toast][data-type="info"] {
  border-color: var(--border);
}

[data-sonner-toaster] [data-sonner-toast][data-type="info"] [data-icon] {
  color: var(--muted-foreground);
}

/* Loading 样式 */
[data-sonner-toaster] [data-sonner-toast][data-type="loading"] {
  border-color: var(--border);
}

[data-sonner-toaster] [data-sonner-toast][data-type="loading"] [data-icon] {
  color: var(--muted-foreground);
}
</style>
