/**
 * VueDotnetBridge 组合式 API 封装
 * 依赖 main.js 中已加载的 bridge.js / bridge-vue.js
 */

import { reactive } from 'vue';

const SERVICE_NAME = 'BridgeTestService';
const SESSION_SERVICE_NAME = 'BridgeTestSessionService';

function createStubStore(message) {
  return reactive({
    _loading: false,
    _error: null,
    _offlineMessage: message,
    Properties: {},
    Collections: {},
    Methods: {},
    Events: {},
    setProperty: () => {},
    _service: null,
    _instanceId: null,
  });
}

function getBridge() {
  return window.DotnetBridge ?? null;
}

function getVueBridge() {
  return window.VueDotnetBridge ?? null;
}

export function isBridgeAvailable() {
  return !!getBridge() && !!window.chrome?.webview;
}

export function useBridgeService(serviceName = SERVICE_NAME) {
  const bridge = getVueBridge();
  if (!bridge || !isBridgeAvailable()) {
    return createStubStore('当前环境未连接 WebView2 bridge');
  }
  return bridge.useService(serviceName);
}

export function useBridgeTransientService(serviceName, paramsSource) {
  const bridge = getVueBridge();
  if (!bridge || !isBridgeAvailable()) {
    return createStubStore('当前环境未连接 WebView2 bridge');
  }
  return bridge.useTransientService(serviceName, paramsSource);
}

export function useBridgeObservableProperty(service, propertyName) {
  const bridge = getVueBridge();
  if (!bridge) return null;
  return bridge.useObservableProperty(service, propertyName);
}

export function useBridgeObservableCollection(service, collectionName) {
  const bridge = getVueBridge();
  if (!bridge) return null;
  return bridge.useObservableCollection(service, collectionName);
}

export function useBridgeServiceEvent(serviceStore, eventName, handler) {
  const bridge = getVueBridge();
  if (!bridge || !serviceStore) return;
  bridge.useServiceEvent(serviceStore, eventName, handler);
}

export async function getBridgeService(serviceName = SERVICE_NAME) {
  const bridge = getBridge();
  if (!bridge) {
    throw new Error('DotnetBridge 未加载或非 WebView2 环境');
  }
  return bridge.getService(serviceName);
}

export function waitForBridgeService(serviceStore, timeoutMs = 5000) {
  if (serviceStore?._service) {
    return Promise.resolve(serviceStore);
  }

  return new Promise((resolve, reject) => {
    const startedAt = Date.now();
    const timer = window.setInterval(() => {
      if (serviceStore?._service) {
        window.clearInterval(timer);
        resolve(serviceStore);
        return;
      }

      if (serviceStore?._error) {
        window.clearInterval(timer);
        reject(new Error(serviceStore._error));
        return;
      }

      if (Date.now() - startedAt > timeoutMs) {
        window.clearInterval(timer);
        reject(new Error('WebView2 服务加载超时'));
      }
    }, 50);
  });
}

export { SERVICE_NAME, SESSION_SERVICE_NAME };
