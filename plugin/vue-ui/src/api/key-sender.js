import { createKeySenderMock } from './key-sender.mock.js';

const SERVICE_NAME = 'KeySenderUiService';

const METHOD_NAMES = [
  'InitializeAsync',
  'GetListAsync',
  'GetDetailAsync',
  'ValidateDraftAsync',
  'CreateNodeAsync',
  'UpdateNodeAsync',
  'CopyNodeAsync',
  'DeleteNodesAsync',
  'GetCategoriesAsync',
  'CreateCategoryAsync',
  'RenameCategoryAsync',
  'DeleteCategoryAsync',
  'MoveNodesAsync',
  'BeginRecordingAsync',
  'StopRecordingAsync',
  'GetRecordingAsync',
  'GetRecordingFragmentAsync',
  'ApplyRecordingAsync',
  'CancelRecordingAsync',
  'GetRunsAsync',
  'CancelRunAsync',
  'GetHeldKeysAsync',
  'ReleaseHeldKeysAsync',
  'PreviewImportAsync',
  'CommitImportAsync',
  'ReleaseImportPreviewAsync',
  'ExportNodesAsync',
];

const EVENT_NAMES = [
  'RecordingChanged',
  'RunsChanged',
  'HeldKeysChanged',
  'NodesChanged',
  'CategoriesChanged',
];

export const DEFAULT_CATEGORY_ID = 'key-sender-default-category';
export const LIST_SCOPE_ALL = 'all';
export const LIST_SCOPE_BUILTIN = 'builtin';
export const LIST_SCOPE_CATEGORY = 'category';
export const NODE_DRAG_MIME = 'application/x-keysender-node';

function hasWebViewBridge() {
  return !!(typeof window !== 'undefined' && window.chrome?.webview && window.DotnetBridge);
}

function createSessionId() {
  if (typeof crypto !== 'undefined' && crypto.randomUUID) return crypto.randomUUID();
  return `sess-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function unwrapResult(raw) {
  if (raw == null) {
    return { ok: false, data: null, error: { code: 'bridge_error', message: '空响应' } };
  }
  if (typeof raw.ok === 'boolean') return raw;
  // Tolerate PascalCase from some serializers
  if (typeof raw.Ok === 'boolean') {
    return {
      ok: raw.Ok,
      data: raw.Data ?? raw.data ?? null,
      error: raw.Error ?? raw.error ?? null,
    };
  }
  return raw;
}

function createRealClient() {
  const windowSessionId = createSessionId();
  let servicePromise = null;
  const unsubscribers = [];

  async function getService() {
    if (!servicePromise) {
      servicePromise = window.DotnetBridge.getService(SERVICE_NAME);
    }
    return servicePromise;
  }

  async function call(methodName, payload = {}) {
    const service = await getService();
    // DotnetBridge.getService 返回的代理把方法挂在根上（与 QuickInput 一致），
    // 不是 VueDotnetBridge.useService 那种 Methods 包装。
    const fn = service?.[methodName];
    if (typeof fn !== 'function') {
      throw new Error(`KeySenderUiService 缺少方法 ${methodName}`);
    }
    const request = { windowSessionId, ...payload };
    const raw = await fn(request);
    return unwrapResult(raw);
  }

  const api = {
    isMock: false,
    windowSessionId,
    injectMockKey() {
      /* real host uses native Hook; no-op */
    },
    resetFixtures() {},
    on(eventName, handler) {
      let disposed = false;
      let listenerId = null;
      let eventApi = null;
      getService()
        .then((service) => {
          if (disposed) return;
          // C# event Foo → JS service.OnFoo.subscribe(...)
          eventApi = service?.[`On${eventName}`];
          if (!eventApi || typeof eventApi.subscribe !== 'function') {
            console.warn(`[KeySender] 无法订阅事件 On${eventName}`);
            return;
          }
          listenerId = eventApi.subscribe((payload) => {
            handler(payload);
          });
        })
        .catch((err) => {
          console.warn(`[KeySender] 订阅 On${eventName} 失败`, err);
        });
      const off = () => {
        disposed = true;
        if (listenerId != null && eventApi && typeof eventApi.unsubscribe === 'function') {
          eventApi.unsubscribe(listenerId);
        }
      };
      unsubscribers.push(off);
      return off;
    },
  };

  for (const name of METHOD_NAMES) {
    api[name] = (payload = {}) => call(name, payload);
  }

  return api;
}

let singleton = null;

/**
 * Bridge facade: WebView2 → KeySenderUiService；浏览器 → in-memory mock。
 * UI 只依赖本模块，不要散落 if (mock)。
 */
export function getKeySenderApi() {
  if (singleton) return singleton;
  singleton = hasWebViewBridge() ? createRealClient() : createKeySenderMock();
  return singleton;
}

export function isKeySenderMock() {
  return getKeySenderApi().isMock;
}

export function withSession(extra = {}) {
  const api = getKeySenderApi();
  return { windowSessionId: api.windowSessionId, ...extra };
}

export { METHOD_NAMES, EVENT_NAMES, SERVICE_NAME, hasWebViewBridge };
