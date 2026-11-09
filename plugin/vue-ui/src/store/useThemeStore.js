import { defineStore } from 'pinia';
import { ref } from 'vue';
import { getThemeVariant, initSystemTheme, setThemeVariant } from '@/lib/colorTheme';

export const useThemeStore = defineStore('theme', () => {
  const theme = ref('light');

  function initTheme() {
    initSystemTheme((variant) => {
      theme.value = variant;
    });
    theme.value = getThemeVariant();
  }

  function toggleTheme() {
    const next = theme.value === 'light' ? 'dark' : 'light';
    setThemeVariant(next);
    theme.value = next;
  }

  return {
    theme,
    initTheme,
    toggleTheme,
  };
});
