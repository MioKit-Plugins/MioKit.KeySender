/**
 * @fileoverview 通过 WebView2 实现 JavaScript 与 .NET 通信的核心桥接层
 * @module bridge
 *
 * 此文件为框架无关核心，react-bridge 和 vue-bridge 共享同一份代码。
 * 它创建 window.DotnetBridge 单例，提供 ServiceProxy（JS Proxy）来代理 .NET 服务调用。
 */

/**
 * @typedef {Object} ServiceProxy
 * @property {string} _serviceName - 服务名称
 * @property {string} [_instanceId] - 实例 ID（用于 Transient 服务）
 * @property {function(string, Array): Promise} call - 调用服务方法
 * @property {function(string, any): void} _updateProperty - 更新属性值
 * @property {Map<string, any>} _propertyValues - 属性值映射
 * @property {Map<string, Set<function>>} _propertySubscribers - 属性变更订阅者
 * @property {function(string, function): string} On[PropertyName].subscribe - 订阅属性/事件变更
 * @property {function(string): void} On[PropertyName].unsubscribe - 取消订阅
 */

/**
 * @typedef {Object} BridgeOptions
 * @property {string} [instanceId] - Transient 服务的实例 ID
 * @property {boolean} [createNewInstance] - 是否强制创建新实例
 * @property {Array} [constructorParameters] - 构造函数参数
 */

/**
 * @typedef {Object} Message
 * @property {string} type - 消息类型（CallMethod、GetService、SubscribeEvent 等）
 * @property {string} messageId - 消息唯一标识
 * @property {string} serviceName - 目标服务名称
 * @property {string} [instanceId] - 实例 ID
 * @property {string} [methodName] - 方法名称
 * @property {Array} [parameters] - 方法参数
 * @property {string} [propertyName] - 属性名称
 * @property {string} [listenerId] - 监听器 ID
 */

let DotnetBridge = (function() {
    /**
     * 用于生成唯一 ID 的计数器
     * @type {number}
     */
    let messageIdCounter = 0;

    /**
     * 等待 .NET 响应的挂起调用
     * @type {Object.<string, {resolve: function, reject: function}>}
     */
    const pendingCalls = {};

    /**
     * 已注册的事件监听器
     * @type {Object.<string, Set<function>>}
     */
    const eventListeners = {};

    /**
     * 已缓存的服务代理
     * @type {Map<string, ServiceProxy>}
     */
    const services = new Map();

    /**
     * 默认 singleton 获取尚未完成时共享同一个 Promise，避免同一服务产生多个 proxy。
     * createNewInstance 和显式 instanceId 不进入该缓存。
     * @type {Map<string, Promise<ServiceProxy>>}
     */
    const pendingSingletonLoads = new Map();

    /**
     * 桥接层是否已就绪
     * @type {boolean}
     */
    let bridgeReady = false;

    /**
     * 桥接就绪后待执行的回调
     * @type {Array<function>}
     */
    let bridgeReadyCallbacks = [];

    /**
     * 生成唯一消息 ID
     * @returns {string} 唯一 ID
     */
    function generateMessageId() {
        messageIdCounter++;
        return 'msg_' + messageIdCounter + '_' + Date.now() + '_' + Math.random().toString(36).substr(2, 9);
    }

    /**
     * 通过 WebView2 向 .NET 发送消息
     * @param {Message} message - 待发送的消息
     * @throws {Error} 发送失败时抛出异常
     */
    function sendMessage(message) {
        try {
            console.log('[DotnetBridge] Sending message:', message);
            window.chrome.webview.postMessage(JSON.stringify(message));
        } catch (error) {
            console.error('[DotnetBridge] Error sending message:', error);
            throw error;
        }
    }

    /**
     * 初始化来自 .NET 的消息监听器
     */
    function initializeMessageListener() {
        if (!window.chrome?.webview) {
            console.warn('[DotnetBridge] Not running in WebView2 environment');
            return;
        }

        window.chrome.webview.addEventListener('message', function(event) {
            try {
                const message = JSON.parse(event.data);
                console.log('[DotnetBridge] Received message:', message.type);
                handleMessage(message);
            } catch (error) {
                console.error('[DotnetBridge] Error parsing message:', error);
            }
        });
    }

    /**
     * 处理来自 .NET 的消息
     * @param {Message} message - 接收到的消息
     */
    function handleMessage(message) {
        switch (message.type) {
            case 'methodResult':
                handleMethodResult(message);
                break;

            case 'eventFired':
                handleEventFired(message);
                break;

            case 'propertyChangeFired':
            case 'PropertyChangeFired':
                handlePropertyChange(message);
                break;

            case 'errorResponse':
                handleErrorResponse(message);
                break;

            default:
                console.warn('[DotnetBridge] Unknown message type:', message.type);
        }
    }

    /**
     * 更新嵌套属性（支持 "User.Name" 形式的路径）
     * @param {Object} obj - 待修改的对象
     * @param {Array<string>} pathParts - 属性路径分段
     * @param {any} newValue - 新值
     * @returns {Object} 更新后的新对象
     */
    function updateNestedProperty(obj, pathParts, newValue) {
        if (pathParts.length === 0) {
            return newValue;
        }

        const [currentProp, ...remainingProps] = pathParts;
        const normalizedProp = currentProp.charAt(0).toLowerCase() + currentProp.slice(1);

        if (typeof obj !== 'object' || obj === null) {
            return newValue;
        }

        const newObj = { ...obj };

        if (remainingProps.length === 0) {
            newObj[normalizedProp] = newValue;
        } else {
            const currentObj = obj[normalizedProp];
            if (currentObj && typeof currentObj === 'object') {
                newObj[normalizedProp] = updateNestedProperty(currentObj, remainingProps, newValue);
            } else {
                newObj[normalizedProp] = newValue;
            }
        }

        return newObj;
    }

    /**
     * 为 .NET 服务创建 JavaScript 代理对象
     * @param {string} serviceName - 服务名称
     * @param {Array<{name: string, value: any, isObservableCollection?: boolean}>} properties - 服务属性元数据
     * @param {string} [instanceId] - 实例 ID
     * @returns {ServiceProxy} 服务代理
     */
    function createServiceProxy(serviceName, properties = [], instanceId = null) {
        const proxy = {
            _serviceName: serviceName,
            _instanceId: instanceId,
            _listeners: new Map(),
            _propertyValues: new Map(),
            _propertySubscribers: new Map(),
            _observableCollections: new Map(),
            _methodCache: new Map()  // 缓存方法引用，保证引用稳定
        };

        properties.forEach(prop => {
            proxy._propertyValues.set(prop.name, prop.value);
            proxy._propertySubscribers.set(prop.name, new Set());
            if (prop.isObservableCollection) {
                proxy._observableCollections.set(prop.name, true);
            }
        });

        /**
         * 调用服务方法
         * @param {string} methodName - 方法名称
         * @param {Array} [parameters] - 方法参数
         * @returns {Promise<any>} 方法返回值
         */
        proxy.call = function(methodName, parameters) {
            return new Promise((resolve, reject) => {
                const messageId = generateMessageId();

                pendingCalls[messageId] = { resolve, reject };

                const message = {
                    type: 'CallMethod',
                    messageId: messageId,
                    serviceName: serviceName,
                    instanceId: instanceId,
                    methodName: methodName,
                    parameters: parameters || []
                };

                sendMessage(message);
            });
        };

        /**
         * 更新本地缓存的属性值并通知订阅者
         * @param {string} propertyPath - 属性路径（如 "User.Name"）
         * @param {any} value - 新值
         */
        proxy._updateProperty = function(propertyPath, value) {
            const pathParts = propertyPath.split('.');

            if (pathParts.length === 1) {
                const propertyName = pathParts[0];
                const oldValue = proxy._propertyValues.get(propertyName);
                proxy._propertyValues.set(propertyName, value);

                const subscribers = proxy._propertySubscribers.get(propertyName);
                console.log("-----------  PROPERTY CHANGED : " + propertyName );
                console.log(subscribers);
                if (subscribers) {
                    subscribers.forEach(callback => {
                        try {
                            callback(value, oldValue);
                        } catch (error) {
                            console.error(`[DotnetBridge] Error in property callback for ${propertyName}:`, error);
                        }
                    });
                }

                // ObservableCollection 还要触发 EventFired 监听器，供 bridge-vue
                // 重新拉取完整集合并更新响应式 Collections。
                if (proxy._observableCollections.get(propertyName)) {
                    const eventName = propertyName + 'Changed';
                    const eventKey = instanceId
                        ? `${serviceName}_${eventName}_${instanceId}`
                        : `${serviceName}_${eventName}`;
                    const eventListenersForCollection = eventListeners[eventKey];
                    if (eventListenersForCollection) {
                        console.log(`[DotnetBridge] Triggering collection changed listeners for ${propertyName}, count: ${eventListenersForCollection.size}`);
                        eventListenersForCollection.forEach(callback => {
                            try {
                                callback(value);
                            } catch (error) {
                                console.error(`[DotnetBridge] Error in collection callback for ${propertyName}:`, error);
                            }
                        });
                    }
                }
            } else {
                const [parentProp, ...nestedProps] = pathParts;
                const oldParentObject = proxy._propertyValues.get(parentProp);

                if (oldParentObject && nestedProps.length > 0) {
                    const newParentObject = updateNestedProperty(oldParentObject, nestedProps, value);
                    proxy._propertyValues.set(parentProp, newParentObject);

                    const subscribers = proxy._propertySubscribers.get(parentProp);
                    console.log(`[DotnetBridge] NESTED PROPERTY CHANGED : ${propertyPath}`);
                    console.log(subscribers);
                    if (subscribers) {
                        subscribers.forEach(callback => {
                            try {
                                callback(newParentObject, oldParentObject);
                            } catch (error) {
                                console.error(`[DotnetBridge] Error in property callback for ${propertyPath}:`, error);
                            }
                        });
                    }
                }
            }
        };

        const methodHandler = {
            get: function(target, propertyName) {
                // 内部属性直接返回（避免被 Proxy 拦截为方法调用）
                if (propertyName === '_serviceName' ||
                    propertyName === '_instanceId' ||
                    propertyName === '_listeners' ||
                    propertyName === 'call' ||
                    propertyName === '_updateProperty' ||
                    propertyName === '_propertyValues' ||
                    propertyName === '_propertySubscribers' ||
                    propertyName === '_propertySubscriptions' ||
                    propertyName === '_observableCollections' ||
                    propertyName === '_methodCache' ||
                    propertyName === '_collectionEventSubscriptions') {
                    return target[propertyName];
                }

                // 避免 Proxy 被当作 Promise 处理
                if (['then', 'catch', 'finally'].includes(propertyName)) {
                    return undefined;
                }

                if (['toString', 'valueOf', 'constructor', 'prototype', 'length', 'name'].includes(propertyName)) {
                    return undefined;
                }

                // OnXxxChanged 事件订阅接口
                if (propertyName && typeof propertyName === 'string' && propertyName.startsWith('On') && propertyName.length > 2) {
                    const eventName = propertyName.slice(2);

                    if (eventName.endsWith('Changed') && eventName.length > 7) {
                        const propName = eventName.slice(0, -7);

                        if (target._propertyValues.has(propName)) {
                            const isObservableCollection = target._observableCollections.get(propName);

                            if (isObservableCollection) {
                                // ObservableCollection：.NET 主动推送 EventFired，仅注册本地 listener
                                return {
                                    subscribe: function(callback) {
                                        return subscribeToLocalEvent(serviceName, eventName, callback, instanceId, target);
                                    },
                                    unsubscribe: function(listenerId) {
                                        unsubscribeFromLocalEvent(target, listenerId);
                                    }
                                };
                            } else {
                                // ObservableProperty：本地订阅属性变更
                                return {
                                    subscribe: function(callback) {
                                        const subscribers = target._propertySubscribers.get(propName);
                                        if (subscribers) {
                                            const listenerId = generateMessageId();
                                            subscribers.add(callback);
                                            target._propertySubscriptions = target._propertySubscriptions || new Map();
                                            target._propertySubscriptions.set(listenerId, { propName, callback });
                                            console.log(`[DotnetBridge] Subscribed to ObservableProperty ${propName} changes via On${propName}Changed`);
                                            return listenerId;
                                        }
                                        return null;
                                    },
                                    unsubscribe: function(listenerId) {
                                        if (target._propertySubscriptions && target._propertySubscriptions.has(listenerId)) {
                                            const { propName, callback } = target._propertySubscriptions.get(listenerId);
                                            const subscribers = target._propertySubscribers.get(propName);
                                            if (subscribers) {
                                                subscribers.delete(callback);
                                                target._propertySubscriptions.delete(listenerId);
                                                console.log(`[DotnetBridge] Unsubscribed from ObservableProperty ${propName} changes`);
                                            }
                                        }
                                    }
                                };
                            }
                        }
                    }

                    // 普通 .NET 事件
                    return {
                        subscribe: function(callback) {
                            return subscribeToEvent(serviceName, eventName, callback, instanceId);
                        },
                        unsubscribe: function(listenerId) {
                            unsubscribeFromEvent(serviceName, eventName, listenerId, instanceId);
                        }
                    };
                }

                // 直接读取已缓存的属性值
                if (target._propertyValues.has(propertyName)) {
                    const propertyValue = target._propertyValues.get(propertyName);
                    subscribeToPropertyChange(serviceName, propertyName);
                    return propertyValue;
                }

                // GetXxx 属性读取器
                if (propertyName && typeof propertyName === 'string' && propertyName.startsWith('Get') && propertyName.length > 3) {
                    const propName = propertyName.substring(3);
                    if (target._propertyValues.has(propName)) {
                        return function(...args) {
                            return new Promise((resolve, reject) => {
                                const messageId = generateMessageId();
                                pendingCalls[messageId] = { resolve, reject };

                                const message = {
                                    type: 'GetProperty',
                                    messageId: messageId,
                                    serviceName: serviceName,
                                    instanceId: instanceId,
                                    propertyName: propName
                                };

                                sendMessage(message);
                            });
                        };
                    }
                }

                // SetXxx 属性写入器
                if (propertyName && typeof propertyName === 'string' && propertyName.startsWith('Set') && propertyName.length > 3) {
                    const propName = propertyName.substring(3);
                    if (target._propertyValues.has(propName)) {
                        return function(value) {
                            return new Promise((resolve, reject) => {
                                const messageId = generateMessageId();
                                pendingCalls[messageId] = { resolve, reject };

                                const message = {
                                    type: 'SetProperty',
                                    messageId: messageId,
                                    serviceName: serviceName,
                                    instanceId: instanceId,
                                    propertyName: propName,
                                    parameters: [value]
                                };

                                sendMessage(message);
                            });
                        };
                    }
                }

                // 从缓存返回稳定的方法引用
                if (target._methodCache.has(propertyName)) {
                    return target._methodCache.get(propertyName);
                }

                // 创建并缓存服务方法
                const cachedMethod = function(...args) {
                    return proxy.call(propertyName, args);
                };
                target._methodCache.set(propertyName, cachedMethod);
                return cachedMethod;
            }
        };

        return new Proxy(proxy, methodHandler);
    }

    /**
     * 处理方法调用结果
     * @param {Object} message - 结果消息
     */
    function handleMethodResult(message) {
        console.log('[DotnetBridge] handleMethodResult called:', message);

        const { messageId, result, success, error } = message;
        const pending = pendingCalls[messageId];

        if (pending) {
            delete pendingCalls[messageId];
            console.log('[DotnetBridge] Resolving pending call for messageId:', messageId);

            if (success) {
                pending.resolve(result);
            } else {
                pending.reject(new Error(error || 'Method call failed'));
            }
        } else {
            console.warn('[DotnetBridge] No pending call found for messageId:', messageId, 'Pending keys:', Object.keys(pendingCalls));
        }
    }

    /**
     * 处理 .NET 触发的事件
     * @param {Object} message - 事件消息
     */
    function handleEventFired(message) {
        const { serviceName, methodName: eventName, result, instanceId } = message;
        const key = getEventListenerKey(serviceName, eventName, instanceId || null);
        const listeners = eventListeners[key];

        if (listeners) {
            listeners.forEach(callback => {
                try {
                    callback(result);
                } catch (error) {
                    console.error('[DotnetBridge] Error in event callback:', error);
                }
            });
        }
    }

    /**
     * 处理属性变更通知
     * @param {Object} message - 属性变更消息
     */
    function handlePropertyChange(message) {
        const { serviceName, methodName: propertyName, result, instanceId } = message;

        const cacheKey = instanceId ? `${serviceName}_${instanceId}` : serviceName;
        const service = services.get(cacheKey);

        if (service && service._updateProperty) {
            const value = result?.value;
            const propertyPath = result?.propertyPath || propertyName;
            console.log(`[DotnetBridge] Property ${propertyPath} changed to:`, value);
            service._updateProperty(propertyPath, value);
        } else {
            console.warn(`[DotnetBridge] Service not found for property change: ${cacheKey}`);
        }
    }

    /**
     * 订阅属性变更（预留扩展）
     * @param {string} serviceName - 服务名称
     * @param {string} propertyName - 属性名称
     */
    function subscribeToPropertyChange(serviceName, propertyName) {
        const key = `${serviceName}_PropertyChange_${propertyName}`;

        if (!eventListeners[key]) {
            eventListeners[key] = new Set();
        }
    }

    /**
     * 处理错误响应
     * @param {Object} message - 错误消息
     */
    function handleErrorResponse(message) {
        const { messageId, error } = message;
        const pending = pendingCalls[messageId];

        if (pending) {
            delete pendingCalls[messageId];
            pending.reject(new Error(error || 'Unknown error'));
        }
    }

    /**
     * 生成本地 EventFired 监听器 key
     * @param {string} serviceName
     * @param {string} eventName
     * @param {string|null|undefined} instanceId
     * @returns {string}
     */
    function getEventListenerKey(serviceName, eventName, instanceId) {
        return instanceId ? `${serviceName}_${eventName}_${instanceId}` : `${serviceName}_${eventName}`;
    }

    /**
     * 订阅 .NET 主动推送的 EventFired（不发送 SubscribeEvent）
     * 用于 ObservableCollection 的 XxxChanged 通知。
     */
    function subscribeToLocalEvent(serviceName, eventName, callback, instanceId, target) {
        const listenerId = generateMessageId();
        const key = getEventListenerKey(serviceName, eventName, instanceId);

        if (!eventListeners[key]) {
            eventListeners[key] = new Set();
        }

        eventListeners[key].add(callback);
        target._collectionEventSubscriptions = target._collectionEventSubscriptions || new Map();
        target._collectionEventSubscriptions.set(listenerId, { key, callback });

        console.log(`[DotnetBridge] Subscribed to ObservableCollection ${eventName} via local EventFired listener`);
        return listenerId;
    }

    /**
     * 取消本地 EventFired 订阅
     */
    function unsubscribeFromLocalEvent(target, listenerId) {
        if (!target._collectionEventSubscriptions || !target._collectionEventSubscriptions.has(listenerId)) {
            return;
        }

        const { key, callback } = target._collectionEventSubscriptions.get(listenerId);
        if (eventListeners[key]) {
            eventListeners[key].delete(callback);
        }
        target._collectionEventSubscriptions.delete(listenerId);
    }

    /**
     * 订阅 .NET 事件
     * @param {string} serviceName - 服务名称
     * @param {string} eventName - 事件名称
     * @param {function} callback - 回调函数
     * @param {string} [instanceId] - 实例 ID
     * @returns {string} 监听器 ID
     */
    function subscribeToEvent(serviceName, eventName, callback, instanceId) {
        console.log('[DotnetBridge] Subscribing to event:', serviceName, eventName, 'instanceId:', instanceId);

        const listenerId = generateMessageId();
        const key = getEventListenerKey(serviceName, eventName, instanceId);

        if (!eventListeners[key]) {
            eventListeners[key] = new Set();
        }

        eventListeners[key].add(callback);

        const message = {
            type: 'SubscribeEvent',
            messageId: listenerId,
            listenerId: listenerId,
            serviceName: serviceName,
            instanceId: instanceId || null,
            methodName: eventName
        };

        pendingCalls[listenerId] = {
            resolve: () => {},
            reject: (error) => console.error(`[DotnetBridge] Failed to subscribe to ${eventName}:`, error)
        };

        sendMessage(message);

        console.log('[DotnetBridge] Subscription sent with listenerId:', listenerId);
        return listenerId;
    }

    /**
     * 取消 .NET 事件订阅
     * @param {string} serviceName - 服务名称
     * @param {string} eventName - 事件名称
     * @param {string} listenerId - 监听器 ID
     * @param {string} [instanceId] - 实例 ID
     */
    function unsubscribeFromEvent(serviceName, eventName, listenerId, instanceId) {
        const key = getEventListenerKey(serviceName, eventName, instanceId);

        if (eventListeners[key]) {
            eventListeners[key].clear();
        }

        const message = {
            type: 'UnsubscribeEvent',
            messageId: listenerId,
            listenerId: listenerId,
            serviceName: serviceName,
            instanceId: instanceId || null,
            methodName: eventName
        };

        sendMessage(message);
    }

    /**
     * 通知桥接层已就绪
     */
    function notifyBridgeReady() {
        bridgeReady = true;
        console.log('[DotnetBridge] Bridge is ready!');

        bridgeReadyCallbacks.forEach(callback => {
            try {
                callback();
            } catch (error) {
                console.error('[DotnetBridge] Error in ready callback:', error);
            }
        });

        bridgeReadyCallbacks = [];
    }

    /**
     * 获取 .NET 服务代理
     * @param {string} serviceName - 服务名称
     * @param {BridgeOptions} [options] - 选项
     * @returns {Promise<ServiceProxy>} 服务代理
     */
    async function getService(serviceName, options = {}) {
        const cacheKey = options.instanceId
            ? `${serviceName}_${options.instanceId}`
            : serviceName;
        const isDefaultSingletonLookup = !options.createNewInstance && !options.instanceId;

        // Singleton 服务复用缓存
        if (isDefaultSingletonLookup && services.has(serviceName)) {
            return services.get(serviceName);
        }

        if (isDefaultSingletonLookup && pendingSingletonLoads.has(serviceName)) {
            return pendingSingletonLoads.get(serviceName);
        }

        if (options.createNewInstance || options.instanceId) {
        } else if (services.has(cacheKey)) {
            return services.get(cacheKey);
        }

        const messageId = generateMessageId();

        const loadPromise = new Promise((resolve, reject) => {
            pendingCalls[messageId] = { resolve, reject };

            const message = {
                type: 'GetService',
                messageId: messageId,
                serviceName: serviceName,
                instanceId: options.instanceId,
                parameters: options.constructorParameters || null
            };

            sendMessage(message);
        }).then(serviceMetadata => {
            const proxy = createServiceProxy(serviceName, serviceMetadata.properties || [], serviceMetadata.instanceId);

            if (serviceMetadata.lifetime === 'Singleton') {
                services.set(serviceName, proxy);
            } else {
                const transientCacheKey = serviceMetadata.instanceId
                    ? `${serviceName}_${serviceMetadata.instanceId}`
                    : `${serviceName}_${Date.now()}_${Math.random()}`;
                services.set(transientCacheKey, proxy);
            }

            console.log('[DotnetBridge] Service loaded:', serviceName);
            console.log(`[DotnetBridge] Métadonnées reçues pour ${serviceName}:`, {
                lifetime: serviceMetadata.lifetime,
                instanceId: serviceMetadata.instanceId,
                methods: serviceMetadata.methods?.map(m => m.name),
                events: serviceMetadata.events,
                properties: serviceMetadata.properties?.map(p => p.name)
            });

            if (!bridgeReady) {
                notifyBridgeReady();
            }

            return proxy;
        });

        if (!isDefaultSingletonLookup) {
            return loadPromise;
        }

        const trackedPromise = loadPromise.finally(() => {
            if (pendingSingletonLoads.get(serviceName) === trackedPromise) {
                pendingSingletonLoads.delete(serviceName);
            }
        });
        pendingSingletonLoads.set(serviceName, trackedPromise);
        return trackedPromise;
    }

    /**
     * 检查桥接层是否已就绪
     * @returns {boolean}
     */
    function isReady() {
        return bridgeReady;
    }

    /**
     * 注册桥接就绪回调
     * @param {function} callback - 就绪后执行的回调
     */
    function onReady(callback) {
        if (bridgeReady) {
            callback();
        } else {
            bridgeReadyCallbacks.push(callback);
        }
    }

    initializeMessageListener();

    const bridge = {
        getService,
        isReady,
        onReady
    };

    if (typeof window !== 'undefined') {
        window.DotnetBridge = bridge;
    }

    return bridge;
})();

// vue-bridge uses window.DotnetBridge global — no ES module export needed
