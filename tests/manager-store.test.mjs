import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';
import { emptyDraft, newEventId, toNodeWrite } from '../plugin/vue-ui/src/api/types.js';
import { moveEvent } from '../plugin/vue-ui/src/lib/event-list.js';
import { createKeySenderMock } from '../plugin/vue-ui/src/api/key-sender.mock.js';

function createStore(api, errors = []) {
  api = { async ValidateDraftAsync() { return { ok: true, data: { valid: true, errors: [], warnings: [] } }; }, ...api };
  const source = readFileSync(new URL('../plugin/vue-ui/src/stores/useManagerStore.js', import.meta.url), 'utf8')
    .replace(/^import[\s\S]*?;\r?\n/gm, '')
    .replace('export const useManagerStore =', 'globalThis.definition =');
  const context = {
    defineStore: (_, definition) => definition,
    LIST_SCOPE_ALL: 'all', LIST_SCOPE_BUILTIN: 'builtin', LIST_SCOPE_CATEGORY: 'category',
    getKeySenderApi: () => api,
    withSession: (extra) => api.windowSessionId ? { windowSessionId: api.windowSessionId, ...extra } : extra,
    emptyDraft, newEventId, toNodeWrite,
    setTimeout, clearTimeout,
    toast: { error(...args) { errors.push(args); }, message() {}, success() {} },
  };
  vm.createContext(context);
  vm.runInContext(source, context);
  const definition = context.definition;
  const store = definition.state();
  for (const [name, action] of Object.entries(definition.actions)) store[name] = action.bind(store);
  for (const [name, getter] of Object.entries(definition.getters))
    Object.defineProperty(store, name, { get: () => getter.call(store, store) });
  store.draft = emptyDraft();
  return store;
}

const review = { recordingId: 'recording', state: 'review', eventCount: 1, sequenceNumber: 2 };
const event = { eventId: 'event', key: 'LeftShift', type: 'keyDown', delayBeforeMs: 0 };

test('review events apply an unpaired recording once, including duplicate notifications', async () => {
  let applyCalls = 0;
  const store = createStore({
    async ApplyRecordingAsync() { applyCalls++; await Promise.resolve(); return { ok: true, data: { events: [event] } }; },
  });
  store.onRecordingChanged(review);
  store.onRecordingChanged(review);
  await store.recordingApplyPromise;
  assert.equal(applyCalls, 1);
  assert.equal(store.draft.sequence.events.length, 1);
  assert.equal(store.draft.sequence.events[0].type, 'keyDown');
  assert.equal(store.recording, null);
});

test('a synchronous review notification during Stop does not apply twice', async () => {
  let applyCalls = 0;
  const store = createStore({
    async StopRecordingAsync() { store.onRecordingChanged(review); return { ok: true, data: review }; },
    async ApplyRecordingAsync() { applyCalls++; return { ok: true, data: { events: [event] } }; },
  });
  store.recording = { ...review, state: 'recording' };
  await store.stopRecording();
  assert.equal(applyCalls, 1);
  assert.equal(store.recording, null);
});

test('an empty completed recording clears the session without altering the draft', async () => {
  let cancelCalls = 0;
  const store = createStore({ async CancelRecordingAsync() { cancelCalls++; return { ok: true }; } });
  store.draft.sequence.events = [event];
  store.onRecordingChanged({ ...review, eventCount: 0 });
  await store.recordingApplyPromise;
  assert.equal(cancelCalls, 1);
  assert.equal(store.recording, null);
  assert.equal(store.draft.sequence.events.length, 1);
});

test('a pairing warning does not interrupt saving a user sequence', async () => {
  let created = 0;
  const store = createStore({
    async ValidateDraftAsync() { return { ok: true, data: { valid: true, errors: [], warnings: [{ code: 'unclosed_key_down' }] } }; },
    async CreateNodeAsync(request) { created++; return { ok: true, data: { nodeId: 'node', canEdit: true, canDelete: true } }; },
    async GetListAsync() { return { ok: true, data: { items: [], total: 0 } }; },
  });
  store.draft.sequence.events = [event];
  assert.equal(await store.saveDraft(), true);
  assert.equal(created, 1);
});

test('rapid edits of 10000 events trigger one validation of the final draft', async () => {
  let calls = 0;
  let finalDelay;
  const store = createStore({
    async ValidateDraftAsync(request) {
      calls++;
      finalDelay = request.detail.sequence.events.at(-1).delayBeforeMs;
      return { ok: true, data: { valid: true, errors: [], warnings: [] } };
    },
  });
  const events = Array.from({ length: 10000 }, (_, i) => ({ ...event, eventId: `event-${i}` }));
  for (let i = 1; i <= 10; i++) store.setEvents([...events.slice(0, -1), { ...events.at(-1), delayBeforeMs: i }]);
  assert.equal(calls, 0);
  await new Promise(resolve => setTimeout(resolve, 250));
  assert.equal(calls, 1);
  assert.equal(finalDelay, 10);
  store.dispose();
});

test('an older validation response cannot replace the current draft diagnostics', async () => {
  const responses = [];
  const store = createStore({ ValidateDraftAsync: () => new Promise(resolve => responses.push(resolve)) });
  const old = store.validateDraftLocal();
  store.setEvents([event]);
  const current = store.validateDraftLocal();
  responses[1]({ ok: true, data: { valid: true, errors: [], warnings: [] } });
  await current;
  responses[0]({ ok: true, data: { valid: false, errors: [{ message: 'outdated' }], warnings: [] } });
  assert.equal(await old, null);
  assert.equal(store.draftErrors.length, 0);
  store.dispose();
});

test('virtual row reordering uses global indexes and preserves all 10000 events', () => {
  const events = Array.from({ length: 10000 }, (_, eventId) => ({ eventId }));
  const moved = moveEvent(events, 5000, 9999);
  assert.equal(moved.length, 10000);
  assert.equal(moved.at(-1).eventId, 5000);
  assert.equal(moved[5000].eventId, 5001);
  assert.equal(new Set(moved.map(event => event.eventId)).size, 10000);
  assert.equal(events[5000].eventId, 5000);
  assert.equal(moveEvent(moved, 9999, 0)[0].eventId, 5000);
});

test('mock recording deduplicates Down and Up per key just like the native recorder', async () => {
  const api = createKeySenderMock();
  api.resetFixtures();
  const session = { windowSessionId: api.windowSessionId };
  for (const run of (await api.GetRunsAsync(session)).data.active)
    await api.CancelRunAsync({ ...session, runId: run.runId });
  const recordingId = (await api.BeginRecordingAsync(session)).data.recordingId;
  for (const [type, key] of [
    ['keyUp', 'LeftShift'], ['keyUp', 'LeftShift'],
    ['keyDown', 'LeftShift'], ['keyDown', 'KeyA'], ['keyDown', 'LeftShift'],
    ['keyUp', 'LeftShift'], ['keyUp', 'LeftShift'], ['keyDown', 'LeftShift'],
  ]) api.injectMockKey(type, key);
  await api.StopRecordingAsync({ ...session, recordingId });
  const fragment = (await api.GetRecordingFragmentAsync({ ...session, recordingId })).data;
  assert.deepEqual(fragment.events.map(event => event.type), ['keyUp', 'keyDown', 'keyDown', 'keyUp', 'keyDown']);
  api.resetFixtures();
});

function deferred() {
  let resolve, reject;
  const promise = new Promise((ok, fail) => { resolve = ok; reject = fail; });
  return { promise, resolve, reject };
}
const savedNode = { nodeId: 'node', name: 'saved', description: '', canEdit: true, canDelete: true };
const listResponse = { ok: true, data: { items: [], total: 0 } };
async function flushPromises() {
  await Promise.resolve(); await Promise.resolve(); await Promise.resolve();
}

test('edits made while a save is pending remain dirty and can be saved again', async () => {
  const pending = deferred();
  const writes = [];
  const store = createStore({
    UpdateNodeAsync(request) {
      writes.push(request.detail);
      return writes.length === 1 ? pending.promise
        : Promise.resolve({ ok: true, data: { ...savedNode, name: request.detail.name } });
    },
    async GetListAsync() { return listResponse; },
  });
  store.draft = emptyDraft({ ...savedNode, name: 'first edit', sequence: { version: 2, events: [event] } });
  const saving = store.saveDraft();
  await flushPromises();
  assert.equal(writes.length, 1);
  store.patchDraft({ name: 'second edit' });
  pending.resolve({ ok: true, data: { ...savedNode, name: 'first edit' } });
  assert.equal(await saving, true);
  assert.equal(store.draft.name, 'second edit');
  assert.equal(store.isDirty, true);
  assert.equal(writes[0].name, 'first edit');
  assert.equal(await store.saveDraft(), true);
  assert.equal(writes.length, 2);
  assert.equal(writes[1].name, 'second edit');
  assert.equal(store.isDirty, false);
  store.dispose();
});

test('a save response cannot attach the old node ID to a new draft', async () => {
  const pending = deferred();
  const store = createStore({
    UpdateNodeAsync() { return pending.promise; },
    async GetListAsync() { return listResponse; },
  });
  store.draft = emptyDraft({ ...savedNode, sequence: { version: 2, events: [event] } });
  const saving = store.saveDraft();
  await flushPromises();
  store.startNewDraft();
  const fresh = store.draft;
  pending.resolve({ ok: true, data: savedNode });
  assert.equal(await saving, true);
  assert.equal(store.draft, fresh);
  assert.equal(store.draft.nodeId, null);
  assert.equal(store.selectedNodeId, null);
  assert.equal(store.isDirty, true);
  store.dispose();
});

test('bridge errors report save failure and keep the draft editable', async () => {
  const errors = [];
  const store = createStore({ async UpdateNodeAsync() { throw new Error('bridge disconnected'); } }, errors);
  store.draft = emptyDraft({ ...savedNode, sequence: { version: 2, events: [event] } });
  assert.equal(await store.saveDraft(), false);
  assert.equal(store.saving, false);
  assert.equal(store.isDirty, true);
  assert.equal(errors[0][0], '保存失败');
  assert.match(errors[0][1].description, /bridge disconnected/);
  store.dispose();
});

test('closing during detail loading prevents a late response from reopening the editor', async () => {
  const pending = deferred();
  const store = createStore({ GetDetailAsync: () => pending.promise });
  const loading = store.selectNode('node');
  store.closeEditor();
  pending.resolve({ ok: true, data: emptyDraft(savedNode) });
  await loading;
  assert.equal(store.draft, null);
  assert.equal(store.selectedNodeId, null);
  assert.equal(store.draftLoading, false);
  store.dispose();
});

test('copying from a dirty editor waits for the save or discard decision', async () => {
  let copied = 0;
  const store = createStore({
    async CopyNodeAsync() { copied++; return { ok: true, data: { nodeId: 'copy' } }; },
    async GetCategoriesAsync() { return { ok: true, data: { items: [] } }; },
    async GetListAsync() { return listResponse; },
    async GetDetailAsync() { return { ok: true, data: emptyDraft({ ...savedNode, nodeId: 'copy' }) }; },
  });
  store.draft = emptyDraft({ ...savedNode, name: 'unsaved edit' });
  await store.copySelectedOrDraft();
  assert.equal(copied, 0);
  assert.equal(store.dialogs.dirtyLeave, true);
  assert.equal(store.dirtyLeaveAction.type, 'copy');
  assert.equal(store.draft.name, 'unsaved edit');
  store.dispose();
});

test('a late recording response cannot replace a new editor session', async () => {
  const pending = deferred();
  const store = createStore({ ApplyRecordingAsync: () => pending.promise });
  store.recording = review;
  const applying = store.doApplyRecording();
  store.draft = emptyDraft({ name: 'another editor session' });
  store.draftGeneration++;
  pending.resolve({ ok: true, data: { events: [event] } });
  await applying;
  assert.equal(store.draft.sequence.events.length, 0);
  store.dispose();
});

test('editing and saving a real mock node survives closing and reopening', async () => {
  const api = createKeySenderMock();
  api.resetFixtures();
  const initial = await api.InitializeAsync({ windowSessionId: api.windowSessionId });
  const node = initial.data.nodes.items.find(item => !item.isBuiltIn);
  const store = createStore(api);
  await store.selectNode(node.nodeId);
  store.patchDraft({ name: '  saved roundtrip  ', description: 'persisted description', beforeDelayMs: 123, afterDelayMs: 456 });
  store.setEvents([{ ...event, delayBeforeMs: 789 }]);
  assert.equal(await store.saveDraft(), true);
  assert.equal(store.draft.name, 'saved roundtrip');
  assert.equal(store.isDirty, false);
  store.closeEditor();
  await store.selectNode(node.nodeId);
  assert.equal(store.draft.name, 'saved roundtrip');
  assert.equal(store.draft.description, 'persisted description');
  assert.equal(store.draft.beforeDelayMs, 123);
  assert.equal(store.draft.afterDelayMs, 456);
  assert.equal(store.draft.sequence.events[0].delayBeforeMs, 789);
  assert.equal(store.isDirty, false);
  store.dispose();
  api.resetFixtures();
});

test('save proceeds independently of an older unfinished diagnostic request', async () => {
  const diagnostic = deferred();
  const store = createStore({
    ValidateDraftAsync: () => diagnostic.promise,
    async UpdateNodeAsync() { return { ok: true, data: savedNode }; },
    async GetListAsync() { return listResponse; },
  });
  store.draft = emptyDraft({ ...savedNode, sequence: { version: 2, events: [event] } });
  const validating = store.validateDraftLocal();
  assert.equal(await store.saveDraft(), true);
  diagnostic.resolve({ ok: true, data: { valid: false, errors: [{ message: 'obsolete' }], warnings: [] } });
  assert.equal(await validating, null);
  assert.equal(store.draftErrors.length, 0);
  store.dispose();
});

test('a save validation failure retains the dirty draft and displays field errors', async () => {
  const store = createStore({ async UpdateNodeAsync() {
    return { ok: false, error: { message: 'invalid delay', details: { errors: [{ fieldPath: 'beforeDelayMs', message: 'invalid delay' }], warnings: [] } } };
  } });
  store.draft = emptyDraft({ ...savedNode, beforeDelayMs: -1 });
  assert.equal(await store.saveDraft(), false);
  assert.equal(store.draft.beforeDelayMs, -1);
  assert.equal(store.isDirty, true);
  assert.equal(store.draftErrors[0].fieldPath, 'beforeDelayMs');
  store.dispose();
});

test('late save errors cannot overwrite diagnostics for newer edits', async () => {
  const pending = deferred();
  const store = createStore({ UpdateNodeAsync: () => pending.promise });
  store.draft = emptyDraft({ ...savedNode, beforeDelayMs: -1 });
  const saving = store.saveDraft();
  store.patchDraft({ beforeDelayMs: 100 });
  await store.validateDraftLocal();
  pending.resolve({ ok: false, error: { details: { errors: [{ message: 'old invalid delay' }] } } });
  assert.equal(await saving, false);
  assert.equal(store.draftErrors.length, 0);
  store.dispose();
});

test('recording prevents leaving or saving the current editor', async () => {
  let writes = 0;
  const store = createStore({ async UpdateNodeAsync() { writes++; } });
  const draft = store.draft;
  store.recordingBusy = true;
  store.startNewDraft();
  await store.requestSelectNode('different');
  store.requestCloseEditor();
  assert.equal(await store.saveDraft(), false);
  assert.equal(store.draft, draft);
  assert.equal(writes, 0);
  store.dispose();
});

test('save and continue closes only after the submitted draft is saved', async () => {
  const store = createStore({
    async UpdateNodeAsync() { return { ok: true, data: savedNode }; },
    async GetListAsync() { return listResponse; },
  });
  store.draft = emptyDraft({ ...savedNode, sequence: { version: 2, events: [event] } });
  store.dirtyLeaveAction = { type: 'close' };
  store.dialogs.dirtyLeave = true;
  await store.dirtyLeaveSave();
  assert.equal(store.draft, null);
  assert.equal(store.dialogs.dirtyLeave, false);
  store.dispose();
});

test('save and continue does not discard edits made during the request', async () => {
  const pending = deferred();
  const store = createStore({
    UpdateNodeAsync: () => pending.promise,
    async GetListAsync() { return listResponse; },
  });
  store.draft = emptyDraft({ ...savedNode, sequence: { version: 2, events: [event] } });
  store.dirtyLeaveAction = { type: 'close' };
  store.dialogs.dirtyLeave = true;
  const saving = store.dirtyLeaveSave();
  store.patchDraft({ description: 'not sent yet' });
  pending.resolve({ ok: true, data: savedNode });
  await saving;
  assert.equal(store.draft.description, 'not sent yet');
  assert.equal(store.isDirty, true);
  assert.equal(store.dialogs.dirtyLeave, true);
  store.dispose();
});
