/**
 * 动态颜色主题 — 供 Avalonia C# 通过 window API 同步 ShadColorTheme
 * 深浅色跟随浏览器 prefers-color-scheme（由后台控制 WebView 主题）
 */

/** @type {string | null} */
let themeName = null;

/** @type {Record<string, unknown> | null} */
let lightPalette = null;

/** @type {Record<string, unknown> | null} */
let darkPalette = null;

/** @type {'light' | 'dark'} */
let currentVariant = 'light';

/** ShadColor 属性 → CSS 变量 */
const CSS_VAR_MAP = {
  background: '--background',
  foreground: '--foreground',
  card: '--card',
  cardForeground: '--card-foreground',
  popover: '--popover',
  popoverForeground: '--popover-foreground',
  primary: '--primary',
  primaryForeground: '--primary-foreground',
  secondary: '--secondary',
  secondaryForeground: '--secondary-foreground',
  muted: '--muted',
  mutedForeground: '--muted-foreground',
  accent: '--accent',
  accentForeground: '--accent-foreground',
  information: '--information',
  informationForeground: '--information-foreground',
  warning: '--warning',
  warningForeground: '--warning-foreground',
  success: '--success',
  successForeground: '--success-foreground',
  danger: '--destructive',
  dangerForeground: '--destructive-foreground',
  border: '--border',
  input: '--input',
  ring: '--ring',
  lowText: '--low-text',
  muteText: '--mute-text',
  disabledText: '--disabled-text',
};

const RADIUS_SIZE_MAP = {
  Small: '4px',
  Normal: '8px',
  Large: '12px',
  small: '4px',
  normal: '8px',
  large: '12px',
};

/**
 * @param {string} key
 */
function toCamelCase(key) {
  if (!key) return key;
  return key.charAt(0).toLowerCase() + key.slice(1);
}

/**
 * 将 Avalonia / CSS 颜色值统一为 CSS color 字符串
 * @param {string | { r: number, g: number, b: number, a?: number }} input
 */
export function normalizeColor(input) {
  if (input == null || input === '') return null;

  if (typeof input === 'string') {
    const hex8 = input.match(/^#([0-9A-Fa-f]{8})$/);
    if (hex8) {
      const a = parseInt(hex8[1].slice(0, 2), 16) / 255;
      const r = parseInt(hex8[1].slice(2, 4), 16);
      const g = parseInt(hex8[1].slice(4, 6), 16);
      const b = parseInt(hex8[1].slice(6, 8), 16);
      if (a >= 0.999) return `rgb(${r}, ${g}, ${b})`;
      return `rgba(${r}, ${g}, ${b}, ${Number(a.toFixed(3))})`;
    }
    return input;
  }

  if (typeof input === 'object') {
    const r = Number(input.r ?? 0);
    const g = Number(input.g ?? 0);
    const b = Number(input.b ?? 0);
    let a = input.a ?? 255;
    if (a <= 1) {
      if (a >= 0.999) return `rgb(${r}, ${g}, ${b})`;
      return `rgba(${r}, ${g}, ${b}, ${Number(a.toFixed(3))})`;
    }
    a = a / 255;
    if (a >= 0.999) return `rgb(${r}, ${g}, ${b})`;
    return `rgba(${r}, ${g}, ${b}, ${Number(a.toFixed(3))})`;
  }

  return String(input);
}

/**
 * @param {Record<string, unknown>} palette
 */
function applyPalette(palette) {
  if (!palette) return;

  const root = document.documentElement;

  for (const [rawKey, value] of Object.entries(palette)) {
    const key = toCamelCase(rawKey);

    if (key === 'radiusSize') {
      const radius = RADIUS_SIZE_MAP[String(value)] ?? RADIUS_SIZE_MAP.Normal;
      root.style.setProperty('--radius', radius);
      continue;
    }

    if (key === 'radius') {
      const radius = typeof value === 'number' ? `${value}px` : String(value);
      root.style.setProperty('--radius', radius);
      continue;
    }

    const cssVar = CSS_VAR_MAP[key];
    if (!cssVar) continue;

    const color = normalizeColor(value);
    if (color) {
      root.style.setProperty(cssVar, color);
    }
  }
}

function getBrowserThemeVariant() {
  return window.matchMedia('(prefers-color-scheme: dark)').matches
    ? 'dark'
    : 'light';
}

function applyCurrentPalette() {
  const palette = currentVariant === 'dark' ? darkPalette : lightPalette;
  applyPalette(palette);
}

function applyDarkClass(variant) {
  const html = document.documentElement;
  if (variant === 'dark') {
    html.classList.add('dark');
  } else {
    html.classList.remove('dark');
  }
}

function dispatchThemeChanged() {
  window.dispatchEvent(
    new CustomEvent('theme-changed', {
      detail: { theme: currentVariant, colorTheme: themeName },
    })
  );
}

function dispatchColorThemeChanged() {
  window.dispatchEvent(
    new CustomEvent('color-theme-changed', {
      detail: { name: themeName },
    })
  );
}

function applyThemeVariant(variant) {
  const changed = variant !== currentVariant;
  currentVariant = variant;
  applyDarkClass(currentVariant);
  applyCurrentPalette();
  if (changed) {
    dispatchThemeChanged();
  }
}

/** 从浏览器主题同步深浅色并应用对应色板 */
function syncBrowserTheme() {
  applyThemeVariant(getBrowserThemeVariant());
}

/**
 * 手动切换深浅色（演示 / 调试用）
 * @param {'light' | 'dark'} variant
 */
export function setThemeVariant(variant) {
  if (variant !== 'light' && variant !== 'dark') return;
  applyThemeVariant(variant);
}

/**
 * 设置完整颜色主题（含 light / dark 两套色板）
 *
 * @param {{
 *   name?: string,
 *   light?: Record<string, unknown>,
 *   dark?: Record<string, unknown>,
 * }} payload
 */
export function setColorTheme(payload) {
  if (!payload || typeof payload !== 'object') {
    console.warn('[colorTheme] setColorTheme: invalid payload');
    return;
  }

  if (payload.name != null) {
    themeName = String(payload.name);
  }
  if (payload.light) {
    lightPalette = payload.light;
  }
  if (payload.dark) {
    darkPalette = payload.dark;
  }

  syncBrowserTheme();
  dispatchColorThemeChanged();
}

/** @returns {'light' | 'dark'} */
export function getThemeVariant() {
  return currentVariant;
}

/** @returns {string | null} */
export function getColorThemeName() {
  return themeName;
}

/** 跟随浏览器 prefers-color-scheme */
export function initSystemTheme(onChange) {
  const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');

  function sync() {
    syncBrowserTheme();
    onChange?.(currentVariant);
  }

  sync();
  mediaQuery.addEventListener('change', sync);
}

/** 挂载 window API，供 C# ExecuteScriptAsync 调用 */
export function mountColorThemeApi() {
  if (typeof window === 'undefined') return;

  window.setColorTheme = setColorTheme;

  const pending = window.__pendingColorThemes;
  if (Array.isArray(pending) && pending.length) {
    pending.forEach((payload) => setColorTheme(payload));
  }
  window.__pendingColorThemes = [];

  window.dispatchEvent(new CustomEvent('color-theme-api-ready'));
}

/** 应用 bootstrap 完成后通知 C#（可选：在此回调中再次 setColorTheme） */
export function notifyAppReady() {
  if (typeof window === 'undefined') return;

  window.dispatchEvent(new CustomEvent('app-ready'));

  if (window.chrome?.webview) {
    try {
      window.chrome.webview.postMessage(
        JSON.stringify({ type: 'appReady', colorTheme: themeName })
      );
    } catch (err) {
      console.warn('[colorTheme] postMessage appReady failed:', err);
    }
  }
}
