/**
 * @fileoverview Vue 3 Composables for DotnetBridge integration
 * @module bridge-vue
 *
 * 依赖全局 Vue 3 与 bridge.js 提供的 DotnetBridge。
 */

(function () {
  'use strict';

  if (typeof Vue === 'undefined') {
    console.error(
      '[VueDotnetBridge] Vue is not loaded. Include Vue 3 before bridge-vue.js'
    );
    return;
  }

  if (typeof DotnetBridge === 'undefined') {
    console.error(
      '[VueDotnetBridge] DotnetBridge is not loaded. Include bridge.js before bridge-vue.js'
    );
    return;
  }

  var ref = Vue.ref;
  var shallowRef = Vue.shallowRef;
  var reactive = Vue.reactive;
  var watch = Vue.watch;
  var onUnmounted = Vue.onUnmounted;

  // ---------------------------------------------------------------------------
  // Helpers
  // ---------------------------------------------------------------------------

  function getPropertyNames(serviceProxy) {
    if (
      !serviceProxy ||
      !serviceProxy._propertyValues ||
      typeof serviceProxy._propertyValues.keys !== 'function'
    ) {
      return [];
    }
    return Array.from(serviceProxy._propertyValues.keys());
  }

  function isCollection(serviceProxy, propertyName) {
    return !!(
      serviceProxy._observableCollections &&
      serviceProxy._observableCollections.has(propertyName)
    );
  }

  function callGetter(serviceProxy, propertyName) {
    var getter = serviceProxy['Get' + propertyName];
    if (typeof getter !== 'function') {
      return Promise.reject(
        new Error('Getter Get' + propertyName + ' not found')
      );
    }
    return Promise.resolve(getter());
  }

  function setPropertyOnService(serviceProxy, propertyName, value) {
    if (!serviceProxy) return;
    var setter = serviceProxy['Set' + propertyName];
    if (typeof setter === 'function') {
      setter(value);
      return;
    }
    console.warn(
      '[VueDotnetBridge] Setter Set' + propertyName + ' not found on service'
    );
  }

  function normalizeEventKey(eventName) {
    var name = String(eventName);
    return name.startsWith('On') ? name : 'On' + name;
  }

  function getEventApi(serviceProxy, eventName) {
    if (!serviceProxy) return null;
    var api = serviceProxy[normalizeEventKey(eventName)];
    return api && typeof api.subscribe === 'function' ? api : null;
  }

  function disposeListeners(serviceProxy, listeners) {
    if (!serviceProxy || !listeners.length) return;

    listeners.forEach(function (item) {
      try {
        var api = serviceProxy[item.eventName];
        if (api && typeof api.unsubscribe === 'function') {
          api.unsubscribe(item.listenerId);
        }
      } catch (e) {
        console.warn('[VueDotnetBridge] Failed to unsubscribe:', e);
      }
    });
  }

  function refreshCollection(state, serviceProxy, propertyName) {
    return callGetter(serviceProxy, propertyName)
      .then(function (data) {
        state.Collections[propertyName] = Array.isArray(data) ? data : [];
      })
      .catch(function (err) {
        console.error(
          '[VueDotnetBridge] Failed to fetch collection ' + propertyName + ':',
          err
        );
      });
  }

  // 合并同一帧内的多次集合变更（例如 Clear + Add），避免暴露中间空状态。
  function scheduleRefreshCollection(state, serviceProxy, propertyName, timers) {
    var existing = timers[propertyName];
    if (existing !== undefined) {
      window.clearTimeout(existing);
    }
    timers[propertyName] = window.setTimeout(function () {
      delete timers[propertyName];
      refreshCollection(state, serviceProxy, propertyName);
    }, 16);
  }

  function createEmptyStore() {
    return reactive({
      Properties: {},
      Collections: {},
      Methods: {},
      Events: {},
      setProperty: function (propertyName, value) {
        setPropertyOnService(this._service, propertyName, value);
      },
      _loading: true,
      _error: null,
      _service: null,
      _instanceId: null,
    });
  }

  function bindServiceProxy(state, serviceProxy, listeners) {
    state._service = serviceProxy;
    state._instanceId = serviceProxy._instanceId || null;
    state.Properties = {};
    state.Collections = {};

    var collectionPromises = [];
    var collectionRefreshTimers = {};

    getPropertyNames(serviceProxy).forEach(function (propertyName) {
      if (isCollection(serviceProxy, propertyName)) {
        state.Collections[propertyName] = [];
        collectionPromises.push(refreshCollection(state, serviceProxy, propertyName));
      } else {
        state.Properties[propertyName] = serviceProxy._propertyValues.get(
          propertyName
        );
      }

      var changedEvent = 'On' + propertyName + 'Changed';
      var eventApi = serviceProxy[changedEvent];
      if (eventApi && typeof eventApi.subscribe === 'function') {
        var listenerId = eventApi.subscribe(function () {
          if (isCollection(serviceProxy, propertyName)) {
            scheduleRefreshCollection(
              state,
              serviceProxy,
              propertyName,
              collectionRefreshTimers
            );
          } else if (arguments.length > 0) {
            state.Properties[propertyName] = arguments[0];
          }
        });
        listeners.push({ eventName: changedEvent, listenerId: listenerId });
      }
    });

    state.Methods = new Proxy(
      {},
      {
        get: function (_target, methodName) {
          if (!state._service || typeof methodName !== 'string') {
            return undefined;
          }
          var method = state._service[methodName];
          return typeof method === 'function' ? method : undefined;
        },
      }
    );

    state.Events = new Proxy(
      {},
      {
        get: function (_target, eventName) {
          return getEventApi(state._service, String(eventName));
        },
      }
    );

    return Promise.all(collectionPromises);
  }

  function unwrapRef(source) {
    if (source && typeof source === 'object' && 'value' in source) {
      return source.value;
    }
    return source;
  }

  function resolveParams(paramsSource) {
    var raw =
      typeof paramsSource === 'function' ? paramsSource() : paramsSource;
    return unwrapRef(raw);
  }

  function toConstructorParameters(params) {
    if (params == null || params === '') return null;
    return Array.isArray(params) ? params : [params];
  }

  // ---------------------------------------------------------------------------
  // useObservableProperty
  // ---------------------------------------------------------------------------

  /**
   * @template T
   * @param {import('./bridge.js').ServiceProxy | null | undefined} service
   * @param {string} propertyName
   * @returns {{ value: import('vue').Ref<T | null>, setValue: (value: T) => void }}
   */
  function useObservableProperty(service, propertyName) {
    var valueRef = ref(null);
    var listenerId = null;
    var currentService = null;
    var currentProperty = null;

    function cleanup() {
      if (listenerId != null && currentService && currentProperty) {
        var eventName = 'On' + currentProperty + 'Changed';
        try {
          currentService[eventName]?.unsubscribe(listenerId);
        } catch (e) {
          console.warn('[useObservableProperty] Cleanup failed:', e);
        }
      }
      listenerId = null;
    }

    function setup() {
      cleanup();

      if (!service || !propertyName) {
        valueRef.value = null;
        currentService = null;
        currentProperty = null;
        return;
      }

      currentService = service;
      currentProperty = propertyName;

      callGetter(service, propertyName)
        .then(function (value) {
          valueRef.value = value;
        })
        .catch(function (err) {
          console.error(
            '[useObservableProperty] Failed to read ' + propertyName + ':',
            err
          );
        });

      var eventApi = service['On' + propertyName + 'Changed'];
      if (eventApi && typeof eventApi.subscribe === 'function') {
        listenerId = eventApi.subscribe(function (newValue) {
          valueRef.value = newValue;
        });
      }
    }

    watch(
      function () {
        return [service, propertyName];
      },
      setup,
      { immediate: true }
    );

    onUnmounted(function () {
      cleanup();
      currentService = null;
      currentProperty = null;
    });

    function setValue(newValue) {
      setPropertyOnService(service, propertyName, newValue);
    }

    return { value: valueRef, setValue: setValue };
  }

  // ---------------------------------------------------------------------------
  // useObservableCollection
  // ---------------------------------------------------------------------------

  /**
   * @template T
   * @param {import('./bridge.js').ServiceProxy | null | undefined} service
   * @param {string} collectionName
   * @returns {import('vue').ShallowRef<T[]>}
   */
  function useObservableCollection(service, collectionName) {
    var collection = shallowRef([]);
    var listenerId = null;
    var currentService = null;
    var currentCollection = null;

    function cleanup() {
      if (listenerId != null && currentService && currentCollection) {
        var eventName = 'On' + currentCollection + 'Changed';
        try {
          currentService[eventName]?.unsubscribe(listenerId);
        } catch (e) {
          console.warn('[useObservableCollection] Cleanup failed:', e);
        }
      }
      listenerId = null;
    }

    function updateCollection() {
      if (!currentService || !currentCollection) return;
      callGetter(currentService, currentCollection)
        .then(function (data) {
          collection.value = Array.isArray(data) ? data : [];
        })
        .catch(function (err) {
          console.error(
            '[useObservableCollection] Failed to read ' +
              currentCollection +
              ':',
            err
          );
        });
    }

    function setup() {
      cleanup();

      if (!service || !collectionName) {
        collection.value = [];
        currentService = null;
        currentCollection = null;
        return;
      }

      currentService = service;
      currentCollection = collectionName;
      updateCollection();

      var eventApi = service['On' + collectionName + 'Changed'];
      if (eventApi && typeof eventApi.subscribe === 'function') {
        listenerId = eventApi.subscribe(updateCollection);
      }
    }

    watch(
      function () {
        return [service, collectionName];
      },
      setup,
      { immediate: true }
    );

    onUnmounted(function () {
      cleanup();
      currentService = null;
      currentCollection = null;
    });

    return collection;
  }

  // ---------------------------------------------------------------------------
  // useService
  // ---------------------------------------------------------------------------

  /**
   * @param {string} serviceName
   * @returns {ReturnType<typeof createEmptyStore>}
   */
  function useService(serviceName) {
    var state = createEmptyStore();
    var listeners = [];
    var loaded = false;
    var loading = false;

    function dispose() {
      disposeListeners(state._service, listeners);
      listeners.length = 0;
    }

    function initialize(serviceProxy) {
      if (loaded) return;
      loaded = true;
      loading = false;
      bindServiceProxy(state, serviceProxy, listeners).then(function () {
        state._loading = false;
        state._error = null;
      });
    }

    function loadService() {
      if (loaded || loading) return;
      loading = true;

      DotnetBridge.getService(serviceName)
        .then(initialize)
        .catch(function (err) {
          loading = false;
          state._loading = false;
          state._error = err.message || 'Failed to load service';
          console.error(
            '[useService] Failed to load "' + serviceName + '":',
            err
          );
        });
    }

    loadService();
    DotnetBridge.onReady(loadService);
    onUnmounted(dispose);

    return state;
  }

  // ---------------------------------------------------------------------------
  // useTransientService
  // ---------------------------------------------------------------------------

  /**
   * 加载 Transient 服务；paramsSource 变化时自动重建实例。
   *
   * @param {string} serviceName
   * @param {import('vue').Ref<any> | (() => any)} paramsSource
   * @returns {ReturnType<typeof createEmptyStore> & { reload: () => void }}
   */
  function useTransientService(serviceName, paramsSource) {
    var state = createEmptyStore();
    var listeners = [];
    var requestId = 0;

    function dispose() {
      disposeListeners(state._service, listeners);
      listeners.length = 0;
      state._service = null;
      state._instanceId = null;
      state.Properties = {};
      state.Collections = {};
    }

    function load(params) {
      var ctorParams = toConstructorParameters(params);
      if (!ctorParams) {
        dispose();
        state._loading = false;
        state._error = null;
        return;
      }

      var currentRequest = ++requestId;
      dispose();
      state._loading = true;
      state._error = null;

      DotnetBridge.getService(serviceName, {
        createNewInstance: true,
        constructorParameters: ctorParams,
      })
        .then(function (serviceProxy) {
          if (currentRequest !== requestId) return;
          bindServiceProxy(state, serviceProxy, listeners).then(function () {
            if (currentRequest !== requestId) return;
            state._loading = false;
          });
        })
        .catch(function (err) {
          if (currentRequest !== requestId) return;
          state._loading = false;
          state._error = err.message || 'Failed to load service';
          console.error(
            '[useTransientService] Failed to load "' +
              serviceName +
              '":',
            err
          );
        });
    }

    state.reload = function () {
      load(resolveParams(paramsSource));
    };

    watch(
      function () {
        return resolveParams(paramsSource);
      },
      function (params) {
        load(params);
      },
      { immediate: true }
    );

    onUnmounted(dispose);
    return state;
  }

  // ---------------------------------------------------------------------------
  // useServiceEvent
  // ---------------------------------------------------------------------------

  /**
   * 订阅 .NET 自定义事件（如 TimerStopped），组件卸载时自动取消。
   *
   * @param {{ _service: import('./bridge.js').ServiceProxy | null }} serviceStore
   * @param {string} eventName
   * @param {(payload?: any) => void} handler
   */
  function useServiceEvent(serviceStore, eventName, handler) {
    var listenerId = null;
    var currentService = null;

    function cleanup() {
      if (listenerId != null && currentService) {
        var api = getEventApi(currentService, eventName);
        if (api) api.unsubscribe(listenerId);
      }
      listenerId = null;
      currentService = null;
    }

    watch(
      function () {
        return serviceStore._service;
      },
      function (serviceProxy) {
        cleanup();
        if (!serviceProxy) return;

        currentService = serviceProxy;
        var api = getEventApi(serviceProxy, eventName);
        if (api) {
          listenerId = api.subscribe(handler);
        }
      },
      { immediate: true }
    );

    onUnmounted(cleanup);
  }

  window.VueDotnetBridge = {
    useObservableProperty: useObservableProperty,
    useObservableCollection: useObservableCollection,
    useService: useService,
    useTransientService: useTransientService,
    useServiceEvent: useServiceEvent,
  };
})();
