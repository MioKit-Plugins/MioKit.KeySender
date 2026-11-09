import { createApp } from 'vue';
import './style.css';
import { mountColorThemeApi, notifyAppReady } from '@/lib/colorTheme';
import { setupVueBridge } from '@/lib/vue-bridge/setup';
import { createPinia } from 'pinia';
import App from './App.vue';
import router from './router';

// 同步挂载，尽早替换 index.html 中的 stub 并回放队列
mountColorThemeApi();

async function bootstrap() {
  await setupVueBridge();

  const pinia = createPinia();
  const app = createApp(App);

  app.use(pinia);
  app.use(router);
  app.mount('#app');

  notifyAppReady();
}

bootstrap();
