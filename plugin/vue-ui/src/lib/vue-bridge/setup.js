import * as Vue from 'vue';

/**
 * 加载 vue-bridge（须动态 import，避免 ES 模块提升导致 window.Vue 未就绪）
 */
export async function setupVueBridge() {
  window.Vue = Vue;
  await import('./bridge.js');
  await import('./bridge-vue.js');
}
