import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { setImmediate } from 'node:timers/promises';
import test from 'node:test';
import vm from 'node:vm';

const bridgeSource = readFileSync(new URL(
  '../plugin/vue-ui/src/lib/vue-bridge/bridge.js', import.meta.url), 'utf8');
const serviceName = 'KeySenderUiService';
let testId = 0;

async function connect(t, replyToMethod = () => null) {
  const outbound = [];
  let receive;
  const sendFromHost = (data) => receive({ data: JSON.stringify(data) });
  const window = {
    chrome: {
      webview: {
        addEventListener(type, handler) {
          assert.equal(type, 'message');
          receive = handler;
        },
        postMessage(raw) {
          const message = JSON.parse(raw);
          outbound.push(message);
          const result = message.type === 'GetService'
            ? { lifetime: 'Singleton', properties: [] }
            : message.type === 'CallMethod' ? replyToMethod(message) : null;
          queueMicrotask(() => sendFromHost({
            type: 'methodResult', messageId: message.messageId, success: true, result,
          }));
        },
      },
    },
  };

  const bridgeErrors = [];
  vm.runInNewContext(bridgeSource, {
    window,
    console: { log() {}, warn() {}, error(...args) { bridgeErrors.push(args); } },
  });
  t.after(() => assert.deepEqual(bridgeErrors, []));
  const previousWindow = globalThis.window;
  globalThis.window = window;
  t.after(() => {
    if (previousWindow === undefined) delete globalThis.window;
    else globalThis.window = previousWindow;
  });
  const entry = new URL('../plugin/vue-ui/src/api/key-sender.js', import.meta.url);
  entry.searchParams.set('test', ++testId);
  const { getKeySenderApi } = await import(entry.href);
  return { api: getKeySenderApi(), outbound, sendFromHost };
}

test('real adapter attaches a stable window session to WebView2 requests and preserves API errors', async (t) => {
  const { api, outbound } = await connect(t, ({ methodName }) => methodName === 'InitializeAsync'
    ? { ok: true, data: { displayName: '模拟按键' } }
    : { ok: false, error: { code: 'window_session_closed', message: '窗口已关闭' } });
  assert.equal(api.isMock, false);
  assert.ok(api.windowSessionId);

  const [initialized, failed] = await Promise.all([
    api.InitializeAsync(),
    api.GetListAsync({ query: 'copy', scope: 'builtin', page: 2 }),
  ]);
  assert.equal(initialized.data.displayName, '模拟按键');
  assert.equal(failed.ok, false);
  assert.equal(failed.error.code, 'window_session_closed');
  assert.equal(outbound.filter(({ type }) => type === 'GetService').length, 1);
  const calls = outbound.filter(({ type }) => type === 'CallMethod');
  assert.deepEqual(calls.map(({ methodName, parameters }) => [methodName, parameters]), [
    ['InitializeAsync', [{ windowSessionId: api.windowSessionId }]],
    ['GetListAsync', [{ windowSessionId: api.windowSessionId, query: 'copy', scope: 'builtin', page: 2 }]],
  ]);
  assert.ok(outbound.every((message) => message.serviceName === serviceName));
});

test('host recording notifications reach the adapter until the subscriber unsubscribes', async (t) => {
  const { api, outbound, sendFromHost } = await connect(t);
  const received = [];
  const off = api.on('RecordingChanged', (snapshot) => received.push(snapshot.state));
  await setImmediate();

  const subscribe = outbound.find(({ type }) => type === 'SubscribeEvent');
  assert.equal(subscribe?.methodName, 'RecordingChanged');
  const emit = (state) => sendFromHost({
    type: 'eventFired', serviceName, methodName: 'RecordingChanged', result: { state },
  });
  emit('recording');
  emit('review');
  off();
  emit('idle');
  await setImmediate();

  assert.deepEqual(received, ['recording', 'review']);
  const unsubscribe = outbound.find(({ type }) => type === 'UnsubscribeEvent');
  assert.ok(subscribe.listenerId);
  assert.equal(unsubscribe?.listenerId, subscribe.listenerId);
});
