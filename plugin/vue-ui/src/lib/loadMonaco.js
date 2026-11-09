// src/lib/loadMonaco.js
// 用于在 WebView2 环境中从虚拟目录加载 Monaco Editor
// MioKit 将用户数据目录中的共享 jsruntime 映射为 jsruntime.local。
// monaco0.54.0 是宿主管理目录；插件扩展 runtime 时应使用自己的唯一子目录。

// 加载状态管理
let monacoLoadPromise = null;

// Monaco 虚拟目录基础路径
const MONACO_BASE = 'https://jsruntime.local/monaco0.54.0/vs';

/**
 * 动态加载 script 标签
 * @param {string} src - script 文件路径
 * @returns {Promise<void>}
 */
function loadScript(src) {
  return new Promise((resolve, reject) => {
    const existingScript = document.querySelector(`script[src="${src}"]`);
    if (existingScript) {
      resolve();
      return;
    }

    const script = document.createElement('script');
    script.src = src;
    script.async = false;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error(`无法加载脚本: ${src}`));
    document.head.appendChild(script);
  });
}

/**
 * 从虚拟目录加载 Monaco Editor
 * @returns {Promise<any>} Monaco 实例
 */
export async function loadMonaco() {
  // 如果已经加载，直接返回
  if (window.monaco) {
    return window.monaco;
  }

  // 如果正在加载中，返回同一个 Promise
  if (monacoLoadPromise) {
    return monacoLoadPromise;
  }

  monacoLoadPromise = (async () => {
    try {
      const loaderUrl = `${MONACO_BASE}/loader.js`;
      console.log('📦 加载 Monaco Loader:', loaderUrl);

      // 加载 AMD loader
      await loadScript(loaderUrl);

      if (!window.require) {
        throw new Error('AMD loader 未正确加载');
      }

      // 配置并加载 Monaco
      return new Promise((resolve, reject) => {
        if (window.require.defined?.('vs/editor/editor.main')) {
          resolve(window.monaco);
          return;
        }

        window.require.config({
          paths: { vs: MONACO_BASE },
          'vs/nls': { availableLanguages: { '*': '' } },
          ignoreDuplicateModules: ['vs/editor/editor.main']
        });

        window.require(['vs/editor/editor.main'], () => {
          if (window.monaco) {
            console.log('✅ Monaco Editor 加载成功 (版本: 0.54.0)');
            resolve(window.monaco);
          } else {
            reject(new Error('Monaco Editor 对象未创建'));
          }
        }, reject);
      });
    } catch (error) {
      console.error('❌ 无法加载 Monaco Editor:', error);
      monacoLoadPromise = null;
      throw error;
    }
  })();

  return monacoLoadPromise;
}
