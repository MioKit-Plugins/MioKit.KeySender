import {
  BUILTIN_COPY_ID,
  BUILTIN_CUT_ID,
  BUILTIN_PASTE_ID,
  delayErrorMessage,
  keyDisplay,
  newEventId,
} from './types.js';

const SESSION_ID = 'mock-window-session';
const MAX_NAME_LENGTH = 100;
const MAX_DESCRIPTION_LENGTH = 500;

function ok(data) {
  return { ok: true, data, error: null };
}

function fail(code, message, extra = {}) {
  return {
    ok: false,
    data: null,
    error: {
      code,
      message,
      fieldPath: extra.fieldPath ?? null,
      eventIndex: extra.eventIndex ?? null,
      nodeId: extra.nodeId ?? null,
      runId: extra.runId ?? null,
      details: extra.details ?? null,
    },
  };
}

function clone(v) {
  return JSON.parse(JSON.stringify(v));
}

/** FNV-1a style fingerprint for mock confirmation handshake */
export function computeWarningFingerprint(detail) {
  const payload = {
    beforeDelayMs: detail.beforeDelayMs ?? 0,
    afterDelayMs: detail.afterDelayMs ?? 0,
    events: (detail.sequence?.events ?? []).map((e) => ({
      type: e.type,
      key: e.key,
      delayBeforeMs: e.delayBeforeMs ?? 0,
    })),
  };
  const s = JSON.stringify(payload);
  let h1 = 2166136261;
  let h2 = 0x811c9dc5;
  for (let i = 0; i < s.length; i += 1) {
    h1 ^= s.charCodeAt(i);
    h1 = Math.imul(h1, 16777619);
    h2 = Math.imul(h2 ^ s.charCodeAt(i), 0x01000193);
  }
  const a = (h1 >>> 0).toString(16).padStart(8, '0');
  const b = (h2 >>> 0).toString(16).padStart(8, '0');
  const c = Math.imul(s.length || 1, 2654435761 >>> 0).toString(16).padStart(8, '0');
  const d = Math.imul(s.charCodeAt(0) || 1, 0x9e3779b9 >>> 0).toString(16).padStart(8, '0');
  return `${a}${b}${c}${d}`;
}

function analyzeSequence(detail) {
  const errors = [];
  const warnings = [];
  const events = detail.sequence?.events ?? [];

  const name = detail.name ?? '';
  if (name.includes('\0')) {
    errors.push({ code: 'invalid_name', fieldPath: 'name', message: '名称含有无效字符' });
  } else if (!name.trim()) {
    errors.push({ code: 'invalid_name', fieldPath: 'name', message: '名称不能为空' });
  } else if (name.trim().length > MAX_NAME_LENGTH) {
    errors.push({ code: 'invalid_name', fieldPath: 'name', message: `名称不能超过 ${MAX_NAME_LENGTH} 个字符` });
  }

  const description = detail.description ?? '';
  if (description.includes('\0')) {
    errors.push({ code: 'invalid_description', fieldPath: 'description', message: '描述含有无效字符' });
  } else if (description.length > MAX_DESCRIPTION_LENGTH) {
    errors.push({ code: 'invalid_description', fieldPath: 'description', message: `描述不能超过 ${MAX_DESCRIPTION_LENGTH} 个字符` });
  }

  for (const [fieldPath, label] of [['beforeDelayMs', '节点前等待'], ['afterDelayMs', '节点后等待']]) {
    const message = delayErrorMessage(detail[fieldPath] ?? 0, label);
    if (message) errors.push({ code: 'invalid_delay', fieldPath, message });
  }

  if (events.length === 0) {
    errors.push({
      code: 'empty_sequence',
      fieldPath: 'sequence.events',
      message: '请至少添加一个按键事件',
    });
  }
  if ((detail.sequence?.version ?? 2) !== 2) {
    errors.push({
      code: 'unsupported_sequence_version',
      fieldPath: 'sequence.version',
      message: '不支持的序列版本',
    });
  }

  const downStack = new Map();
  events.forEach((ev, index) => {
    if (ev.type !== 'keyDown' && ev.type !== 'keyUp') {
      errors.push({
        code: 'validation_failed',
        fieldPath: `sequence.events[${index}].type`,
        eventIndex: index,
        message: `未知事件类型：${ev.type}`,
      });
      return;
    }
    if (!ev.key) {
      errors.push({
        code: 'unsupported_key',
        fieldPath: `sequence.events[${index}].key`,
        eventIndex: index,
        message: '按键不能为空',
      });
      return;
    }
    const delayError = delayErrorMessage(Number(ev.delayBeforeMs), `第 ${index + 1} 条事件的等待`);
    if (delayError) {
      errors.push({
        code: 'invalid_delay',
        fieldPath: `sequence.events[${index}].delayBeforeMs`,
        eventIndex: index,
        message: delayError,
      });
    }

    if (ev.type === 'keyDown') {
      if (downStack.has(ev.key)) {
        warnings.push({
          code: 'repeated_keydown',
          key: ev.key,
          eventIndex: index,
          message: `${keyDisplay(ev.key)} 已在本序列按下时再次按下`,
        });
      }
      downStack.set(ev.key, index);
    } else if (!downStack.has(ev.key)) {
      warnings.push({
        code: 'unmatched_keyup',
        key: ev.key,
        eventIndex: index,
        message: `${keyDisplay(ev.key)} 抬起在本序列内没有对应按下`,
      });
    } else {
      downStack.delete(ev.key);
    }
  });

  for (const [key, eventIndex] of downStack.entries()) {
    warnings.push({
      code: 'unclosed_keydown',
      key,
      eventIndex,
      message: `${keyDisplay(key)} 未释放`,
    });
  }

  const warningFingerprint = warnings.length
    ? computeWarningFingerprint(detail)
    : null;

  return {
    valid: errors.length === 0,
    errors,
    warnings,
    warningFingerprint,
  };
}

function ctrlChord(mainKey) {
  return {
    version: 2,
    events: [
      { eventId: newEventId(), type: 'keyDown', key: 'LeftControl', delayBeforeMs: 0 },
      { eventId: newEventId(), type: 'keyDown', key: mainKey, delayBeforeMs: 0 },
      { eventId: newEventId(), type: 'keyUp', key: mainKey, delayBeforeMs: 0 },
      { eventId: newEventId(), type: 'keyUp', key: 'LeftControl', delayBeforeMs: 0 },
    ],
  };
}

function findCategory(categoryId) {
  return state.categories.find((c) => c.categoryId === categoryId) ?? null;
}

function listItemFrom(node) {
  const category = node.categoryId ? findCategory(node.categoryId) : null;
  return {
    nodeId: node.nodeId,
    name: node.name,
    description: node.description,
    aliasNames: node.aliasNames ?? [],
    hotkeyDisplay: node.hotkeyDisplay ?? '',
    isPinned: !!node.isPinned,
    dragText: node.dragText ?? '',
    isBuiltIn: !!node.isBuiltIn,
    isReadOnly: !!node.isReadOnly,
    canEdit: !!node.canEdit,
    canDelete: !!node.canDelete,
    categoryId: node.isBuiltIn ? null : (node.categoryId ?? null),
    categoryName: node.isBuiltIn ? null : (category?.name ?? null),
  };
}

function buildCategoryList() {
  const items = state.categories
    .map((c) => ({
      categoryId: c.categoryId,
      name: c.name,
      description: c.description ?? '',
      nodeCount: state.nodes.filter((n) => !n.isBuiltIn && n.categoryId === c.categoryId).length,
      canRename: true,
      canDelete: true,
    }))
    .sort((a, b) => {
      const byName = a.name.localeCompare(b.name, undefined, { sensitivity: 'accent' });
      if (byName !== 0) return byName;
      return a.categoryId.localeCompare(b.categoryId);
    });
  return {
    items,
    sequenceNumber: state.categoriesSeq,
  };
}

function bumpCategories(kind, categoryIds) {
  state.categoriesSeq += 1;
  emit('CategoriesChanged', {
    kind,
    categoryIds,
    sequenceNumber: state.categoriesSeq,
  });
}

function resolveTargetCategoryId(categoryId) {
  if (!categoryId) return null; // uncategorized
  return findCategory(categoryId) ? categoryId : undefined;
}

function buildKeyCatalog() {
  const entries = [];
  const push = (key, display, group) => {
    entries.push({ key, display: display ?? (keyDisplay(key) || key), group });
  };

  ;['LeftControl', 'RightControl', 'LeftShift', 'RightShift', 'LeftAlt', 'RightAlt', 'LeftWin', 'RightWin']
    .forEach((k) => push(k, keyDisplay(k), '修饰键'));
  for (let i = 65; i <= 90; i += 1) {
    const letter = String.fromCharCode(i);
    push(`Key${letter}`, letter, '字母');
  }
  for (let i = 0; i <= 9; i += 1) push(`Digit${i}`, String(i), '数字');
  for (let i = 1; i <= 12; i += 1) push(`F${i}`, `F${i}`, '功能键');
  ;[
    'Escape', 'Tab', 'CapsLock', 'Space', 'Enter', 'Backspace',
    'ArrowLeft', 'ArrowUp', 'ArrowRight', 'ArrowDown',
    'Home', 'End', 'PageUp', 'PageDown', 'Insert', 'Delete',
    'PrintScreen', 'ScrollLock', 'Pause', 'NumLock', 'ContextMenu',
  ].forEach((k) => push(k, keyDisplay(k), '其它'));
  for (let i = 0; i <= 9; i += 1) push(`Numpad${i}`, `小键盘 ${i}`, '小键盘');
  ;['NumpadMultiply', 'NumpadAdd', 'NumpadSubtract', 'NumpadDecimal', 'NumpadDivide', 'NumpadEnter']
    .forEach((k) => push(k, keyDisplay(k) || k, '小键盘'));

  return entries;
}

function createInitialState() {
  const userShiftOnlyId = 'A1000001-0000-4000-8000-000000000001';
  const userInterleavedId = 'A1000001-0000-4000-8000-000000000002';
  const workCategoryId = 'A2000001-0000-4000-8000-000000000010';

  const categories = [
    {
      categoryId: workCategoryId,
      name: '办公',
      description: '办公相关快捷操作',
    },
  ];

  const nodes = [
    {
      nodeId: BUILTIN_COPY_ID,
      name: '复制',
      description: '向目标窗口发送 Ctrl+C',
      aliasNames: [],
      hotkeyDisplay: '',
      isPinned: false,
      dragText: '复制',
      isBuiltIn: true,
      isReadOnly: true,
      canEdit: false,
      canDelete: false,
      categoryId: null,
      beforeDelayMs: 0,
      afterDelayMs: 0,
      sequence: ctrlChord('KeyC'),
    },
    {
      nodeId: BUILTIN_CUT_ID,
      name: '剪切',
      description: '向目标窗口发送 Ctrl+X',
      aliasNames: [],
      hotkeyDisplay: '',
      isPinned: false,
      dragText: '剪切',
      isBuiltIn: true,
      isReadOnly: true,
      canEdit: false,
      canDelete: false,
      categoryId: null,
      beforeDelayMs: 0,
      afterDelayMs: 0,
      sequence: ctrlChord('KeyX'),
    },
    {
      nodeId: BUILTIN_PASTE_ID,
      name: '粘贴',
      description: '向目标窗口发送 Ctrl+V',
      aliasNames: [],
      hotkeyDisplay: '',
      isPinned: false,
      dragText: '粘贴',
      isBuiltIn: true,
      isReadOnly: true,
      canEdit: false,
      canDelete: false,
      categoryId: null,
      beforeDelayMs: 0,
      afterDelayMs: 0,
      sequence: ctrlChord('KeyV'),
    },
    {
      nodeId: userShiftOnlyId,
      name: '按住左 Shift',
      description: '仅按下 LeftShift，结束后保持',
      aliasNames: ['shift down'],
      hotkeyDisplay: '',
      isPinned: false,
      dragText: '按住左 Shift',
      isBuiltIn: false,
      isReadOnly: false,
      canEdit: true,
      canDelete: true,
      categoryId: null,
      beforeDelayMs: 0,
      afterDelayMs: 0,
      sequence: {
        version: 2,
        events: [
          { eventId: newEventId(), type: 'keyDown', key: 'LeftShift', delayBeforeMs: 0 },
        ],
      },
    },
    {
      nodeId: userInterleavedId,
      name: '交错 A/B',
      description: 'A Down → B Down → A Up → B Up',
      aliasNames: [],
      hotkeyDisplay: 'Ctrl+Shift+B',
      isPinned: true,
      dragText: '交错 A/B',
      isBuiltIn: false,
      isReadOnly: false,
      canEdit: true,
      canDelete: true,
      categoryId: workCategoryId,
      beforeDelayMs: 100,
      afterDelayMs: 50,
      sequence: {
        version: 2,
        events: [
          { eventId: newEventId(), type: 'keyDown', key: 'KeyA', delayBeforeMs: 0 },
          { eventId: newEventId(), type: 'keyDown', key: 'KeyB', delayBeforeMs: 40 },
          { eventId: newEventId(), type: 'keyUp', key: 'KeyA', delayBeforeMs: 30 },
          { eventId: newEventId(), type: 'keyUp', key: 'KeyB', delayBeforeMs: 20 },
        ],
      },
    },
  ];

  return {
    categories,
    categoriesSeq: 1,
    nodes,
    nodesSeq: 1,
    runsSeq: 1,
    recordingSeq: 1,
    runs: {
      sequenceNumber: 1,
      active: [
        {
          runId: 'run-queued-1',
          nodeId: userInterleavedId,
          nodeName: '交错 A/B',
          state: 'queued',
          eventIndex: null,
          eventCount: null,
          queuePosition: 1,
          submittedAt: new Date().toISOString(),
          startedAt: null,
          finishedAt: null,
          isCancellationRequested: false,
          error: null,
        },
      ],
      recent: [],
    },
    recording: null,
    recordingFragment: null,
    importPreview: null,
    listeners: {
      RecordingChanged: new Set(),
      RunsChanged: new Set(),
      NodesChanged: new Set(),
      CategoriesChanged: new Set(),
      FocusNodeRequested: new Set(),
    },
  };
}

let state = createInitialState();

function emit(eventName, payload) {
  for (const fn of state.listeners[eventName] ?? []) {
    try {
      fn(payload);
    } catch {
      /* ignore listener errors in mock */
    }
  }
}

function requireSession(request) {
  if (!request?.windowSessionId) {
    return fail('window_session_closed', '缺少 windowSessionId');
  }
  return null;
}

function pageNodes(query = '', page = 1, pageSize = 20, scope = 'all', categoryId = null) {
  const q = String(query ?? '').trim().toLowerCase();
  const normalizedScope = String(scope ?? 'all').toLowerCase();
  let items = state.nodes.map(listItemFrom);
  if (normalizedScope === 'builtin') {
    items = items.filter((n) => n.isBuiltIn);
  } else if (normalizedScope === 'category') {
    items = items.filter((n) => !n.isBuiltIn && n.categoryId === categoryId);
  }
  if (q) {
    items = items.filter((n) => {
      const hay = [n.name, n.description, ...(n.aliasNames ?? [])].join('\n').toLowerCase();
      return hay.includes(q);
    });
  }
  items.sort((a, b) => {
    const byName = a.name.localeCompare(b.name, undefined, { sensitivity: 'accent' });
    if (byName !== 0) return byName;
    return a.nodeId.localeCompare(b.nodeId);
  });
  const total = items.length;
  const size = Math.min(100, Math.max(1, pageSize || 20));
  const p = Math.max(1, page || 1);
  const start = (p - 1) * size;
  return {
    items: items.slice(start, start + size),
    total,
    page: p,
    pageSize: size,
    sequenceNumber: state.nodesSeq,
  };
}

function bumpNodes(kind, nodeIds) {
  state.nodesSeq += 1;
  emit('NodesChanged', {
    kind,
    nodeIds,
    sequenceNumber: state.nodesSeq,
  });
}

function snapshotRuns() {
  state.runs.sequenceNumber = state.runsSeq;
  return clone(state.runs);
}

function findNode(nodeId) {
  return state.nodes.find((n) => n.nodeId === nodeId) ?? null;
}

export function createKeySenderMock() {
  return {
    isMock: true,
    windowSessionId: SESSION_ID,

    on(eventName, handler) {
      if (!state.listeners[eventName]) return () => {};
      state.listeners[eventName].add(handler);
      return () => state.listeners[eventName].delete(handler);
    },

    /** Demo helper: inject mock key into current recording */
    injectMockKey(type, key) {
      if (!state.recording || state.recording.state !== 'recording') return;
      if (state.recordingKeyStates.get(key) === type) return;
      state.recordingKeyStates.set(key, type);
      if (!state.recordingFragment) {
        state.recordingFragment = { recordingId: state.recording.recordingId, events: [], notices: [] };
      }
      state.recordingFragment.events.push({
        eventId: newEventId(),
        type,
        key,
        delayBeforeMs: 0,
      });
      state.recording.eventCount = state.recordingFragment.events.length;
      state.recording.sequenceNumber = (state.recordingSeq += 1);
      emit('RecordingChanged', clone(state.recording));
    },

    resetFixtures() {
      state = createInitialState();
    },

    async InitializeAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      return ok({
        displayName: '按键发送（Mock）',
        limits: {
          maxEvents: 10000,
          maxPageSize: 100,
          defaultPageSize: 20,
          maxNameLength: MAX_NAME_LENGTH,
          maxDescriptionLength: MAX_DESCRIPTION_LENGTH,
        },
        keyCatalog: buildKeyCatalog(),
        categories: buildCategoryList().items,
        nodes: pageNodes('', 1, 20, 'all', null),
        runs: snapshotRuns(),
      });
    },

    async GetListAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      return ok(pageNodes(
        request.query,
        request.page,
        request.pageSize,
        request.scope,
        request.categoryId,
      ));
    },

    async GetCategoriesAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      return ok(buildCategoryList());
    },

    async CreateCategoryAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      const name = String(request.name ?? '').trim();
      if (!name) return fail('invalid_name', '分类名称不合法');
      const category = {
        categoryId: newEventId().toUpperCase(),
        name,
        description: String(request.description ?? '').trim(),
      };
      state.categories.push(category);
      bumpCategories('created', [category.categoryId]);
      return ok(buildCategoryList().items.find((c) => c.categoryId === category.categoryId));
    },

    async RenameCategoryAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      const category = findCategory(request.categoryId);
      if (!category) return fail('category_not_found', '分类不存在');
      const name = String(request.name ?? '').trim();
      if (!name) return fail('invalid_name', '分类名称不合法');
      category.name = name;
      if (request.description != null) {
        category.description = String(request.description).trim();
      }
      bumpCategories('updated', [category.categoryId]);
      return ok(buildCategoryList().items.find((c) => c.categoryId === category.categoryId));
    },

    async DeleteCategoryAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      const category = findCategory(request.categoryId);
      if (!category) return fail('category_not_found', '分类不存在');
      const deletedIds = state.nodes
        .filter((n) => !n.isBuiltIn && n.categoryId === category.categoryId)
        .map((n) => n.nodeId);
      state.nodes = state.nodes.filter((n) => !deletedIds.includes(n.nodeId));
      state.categories = state.categories.filter((c) => c.categoryId !== category.categoryId);
      if (deletedIds.length) bumpNodes('deleted', deletedIds);
      bumpCategories('deleted', [category.categoryId]);
      return ok({ deletedIds });
    },

    async MoveNodesAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      const target = findCategory(request.targetCategoryId);
      if (!target) return fail('category_not_found', '分类不存在');
      const ids = (request.nodeIds ?? []).filter(Boolean);
      for (const id of ids) {
        const node = findNode(id);
        if (!node) return fail('node_not_found', '节点不存在', { nodeId: id });
        if (node.isBuiltIn) return fail('builtin_read_only', '内置节点不能移动');
        node.categoryId = target.categoryId;
      }
      bumpNodes('moved', ids);
      bumpCategories('updated', [target.categoryId]);
      return ok(true);
    },

    async GetDetailAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      const node = findNode(request.nodeId);
      if (!node) return fail('node_not_found', '节点不存在', { nodeId: request.nodeId });
      return ok({
        nodeId: node.nodeId,
        name: node.name,
        description: node.description,
        beforeDelayMs: node.beforeDelayMs,
        afterDelayMs: node.afterDelayMs,
        sequence: clone(node.sequence),
        isBuiltIn: node.isBuiltIn,
        isReadOnly: node.isReadOnly,
        canEdit: node.canEdit,
        canDelete: node.canDelete,
      });
    },

    async ValidateDraftAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      return ok(analyzeSequence(request.detail ?? {}));
    },

    async CreateNodeAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      const detail = request.detail ?? {};
      const validation = analyzeSequence(detail);
      if (!validation.valid) {
        return fail('validation_failed', '校验失败', {
          details: { errors: validation.errors, warnings: validation.warnings },
        });
      }
      const categoryId = resolveTargetCategoryId(request.categoryId);
      if (categoryId === undefined) {
        return fail('category_not_found', '分类不存在');
      }
      const node = {
        nodeId: newEventId().toUpperCase(),
        name: detail.name.trim(),
        description: detail.description ?? '',
        aliasNames: [],
        hotkeyDisplay: '',
        isPinned: false,
        dragText: detail.name.trim(),
        isBuiltIn: false,
        isReadOnly: false,
        canEdit: true,
        canDelete: true,
        categoryId,
        beforeDelayMs: detail.beforeDelayMs ?? 0,
        afterDelayMs: detail.afterDelayMs ?? 0,
        sequence: clone(detail.sequence),
      };
      state.nodes.push(node);
      bumpNodes('created', [node.nodeId]);
      bumpCategories('updated', [categoryId]);
      return ok(listItemFrom(node));
    },

    async UpdateNodeAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      const node = findNode(request.nodeId);
      if (!node) return fail('node_not_found', '节点不存在', { nodeId: request.nodeId });
      if (node.isBuiltIn) return fail('builtin_read_only', '内置节点只读，不能修改');
      const detail = request.detail ?? {};
      const validation = analyzeSequence(detail);
      if (!validation.valid) {
        return fail('validation_failed', '校验失败', {
          details: { errors: validation.errors, warnings: validation.warnings },
        });
      }
      node.name = detail.name.trim();
      node.description = detail.description ?? '';
      node.beforeDelayMs = detail.beforeDelayMs ?? 0;
      node.afterDelayMs = detail.afterDelayMs ?? 0;
      node.sequence = clone(detail.sequence);
      node.dragText = node.name;
      bumpNodes('updated', [node.nodeId]);
      return ok(listItemFrom(node));
    },

    async CopyNodeAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      const source = findNode(request.nodeId);
      if (!source) return fail('node_not_found', '节点不存在', { nodeId: request.nodeId });
      const write = {
        name: `${source.name} 副本`.slice(0, 100),
        description: source.description,
        beforeDelayMs: source.beforeDelayMs,
        afterDelayMs: source.afterDelayMs,
        sequence: {
          version: 2,
          events: (source.sequence?.events ?? []).map((e) => ({
            ...e,
            eventId: newEventId(),
          })),
        },
      };
      const validation = analyzeSequence(write);
      const categoryId = resolveTargetCategoryId(
        request.categoryId ?? source.categoryId ?? null,
      );
      if (categoryId === undefined) {
        return fail('category_not_found', '分类不存在');
      }
      const node = {
        ...write,
        nodeId: newEventId().toUpperCase(),
        aliasNames: [],
        hotkeyDisplay: '',
        isPinned: false,
        dragText: write.name,
        isBuiltIn: false,
        isReadOnly: false,
        canEdit: true,
        canDelete: true,
        categoryId,
      };
      state.nodes.push(node);
      bumpNodes('created', [node.nodeId]);
      bumpCategories('updated', [categoryId]);
      return ok(listItemFrom(node));
    },

    async DeleteNodesAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      const ids = request.nodeIds ?? [];
      if (ids.some((id) => findNode(id)?.isBuiltIn)) {
        return fail('builtin_read_only', '不能删除内置节点');
      }
      const deletedIds = [];
      state.nodes = state.nodes.filter((n) => {
        if (ids.includes(n.nodeId)) {
          deletedIds.push(n.nodeId);
          return false;
        }
        return true;
      });
      bumpNodes('deleted', deletedIds);
      return ok({ deletedIds });
    },

    async BeginRecordingAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      if (state.recording && !['cancelled', 'failed', 'review'].includes(state.recording.state)) {
        return fail('recording_busy', '已有录制进行中');
      }
      if (state.runs.active.length) {
        return fail('execution_busy', '有任务进行中，无法开始录制');
      }
      const recordingId = newEventId();
      state.recordingKeyStates = new Map();
      state.recordingFragment = { recordingId, events: [], notices: [] };
      state.recording = {
        recordingId,
        windowSessionId: request.windowSessionId,
        state: 'recording',
        eventCount: 0,
        elapsedMs: 0,
        sequenceNumber: (state.recordingSeq += 1),
        notices: [],
        error: null,
      };
      emit('RecordingChanged', clone(state.recording));
      return ok(clone(state.recording));
    },

    async StopRecordingAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      if (!state.recording || state.recording.recordingId !== request.recordingId) {
        return fail('recording_not_ready', '录制会话不存在');
      }
      if (state.recording.state === 'review') return ok(clone(state.recording));
      state.recording.state = 'review';
      state.recording.sequenceNumber = (state.recordingSeq += 1);
      emit('RecordingChanged', clone(state.recording));
      return ok(clone(state.recording));
    },

    async GetRecordingFragmentAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      if (!state.recording || state.recording.recordingId !== request.recordingId) {
        return fail('recording_not_ready', '录制会话不存在');
      }
      if (state.recording.state !== 'review') {
        return fail('recording_not_ready', '录制尚未进入审阅');
      }
      return ok(clone(state.recordingFragment ?? {
        recordingId: request.recordingId,
        events: [],
        notices: [],
      }));
    },

    async ApplyRecordingAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      if (!state.recording || state.recording.recordingId !== request.recordingId) {
        return fail('recording_not_ready', '录制会话不存在');
      }
      if (state.recording.state !== 'review') {
        return fail('recording_not_ready', '录制尚未进入审阅');
      }
      const fragment = clone(state.recordingFragment ?? {
        recordingId: request.recordingId,
        events: [],
        notices: [],
      });
      if (!fragment.events.length) {
        return fail('recording_not_ready', '录制片段为空');
      }
      state.recording = null;
      state.recordingFragment = null;
      emit('RecordingChanged', null);
      return ok(fragment);
    },

    async CancelRecordingAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      if (state.recording && state.recording.recordingId === request.recordingId) {
        state.recording = {
          ...state.recording,
          state: 'cancelled',
          sequenceNumber: (state.recordingSeq += 1),
        };
        emit('RecordingChanged', clone(state.recording));
      }
      state.recording = null;
      state.recordingFragment = null;
      return ok({
        recordingId: request.recordingId,
        windowSessionId: request.windowSessionId,
        state: 'cancelled',
        eventCount: 0,
        elapsedMs: 0,
        sequenceNumber: state.recordingSeq,
        notices: [],
        error: null,
      });
    },

    async GetRunsAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      return ok(snapshotRuns());
    },

    async CancelRunAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      const idx = state.runs.active.findIndex((r) => r.runId === request.runId);
      if (idx < 0) return fail('node_not_found', '任务不存在', { runId: request.runId });
      const [run] = state.runs.active.splice(idx, 1);
      run.state = 'cancelled';
      run.finishedAt = new Date().toISOString();
      run.isCancellationRequested = true;
      state.runs.recent = [run, ...state.runs.recent].slice(0, 50);
      state.runsSeq += 1;
      emit('RunsChanged', snapshotRuns());
      return ok({ cancellationRequested: true });
    },

    async PreviewImportAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      if (state.recording) {
        return fail('recording_busy', '录制进行中不能导入');
      }
      const previewId = newEventId();
      const categories = [{ name: '导入示例分类', description: '分类说明示例' }];
      const items = [
        {
          itemIndex: 0,
          name: '导入示例 · Down-only',
          description: '仅 LeftControl Down',
          categoryName: categories[0].name,
          eventCount: 1,
          totalDelayMs: 0,
          errors: [],
          warnings: [
            {
              code: 'unclosed_keydown',
              key: 'LeftControl',
              eventIndex: 0,
              message: '左 Ctrl 未释放',
            },
          ],
        },
        {
          itemIndex: 1,
          name: '导入示例 · 平衡',
          description: 'Ctrl+C',
          eventCount: 4,
          totalDelayMs: 100,
          errors: [],
          warnings: [],
        },
      ];
      const warningFingerprint = computeWarningFingerprint({
        beforeDelayMs: 0,
        afterDelayMs: 0,
        sequence: {
          version: 2,
          events: [
            { type: 'keyDown', key: 'LeftControl', delayBeforeMs: 0 },
          ],
        },
      });
      state.importPreview = {
        previewId,
        expiresAt: new Date(Date.now() + 10 * 60 * 1000).toISOString(),
        itemCount: items.length,
        canImport: true,
        warningFingerprint,
        items,
        _categories: categories,
        _payloads: [
          {
            name: '导入示例 · Down-only',
            description: '仅 LeftControl Down',
            categoryIndex: 0,
            beforeDelayMs: 0,
            afterDelayMs: 0,
            sequence: {
              version: 2,
              events: [
                { eventId: newEventId(), type: 'keyDown', key: 'LeftControl', delayBeforeMs: 0 },
              ],
            },
          },
          {
            name: '导入示例 · 平衡',
            description: 'Ctrl+C',
            beforeDelayMs: 0,
            afterDelayMs: 0,
            sequence: ctrlChord('KeyC'),
          },
        ],
      };
      return ok({
        previewId,
        expiresAt: state.importPreview.expiresAt,
        itemCount: items.length,
        canImport: true,
        warningFingerprint,
        items,
      });
    },

    async CommitImportAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      const preview = state.importPreview;
      if (!preview || preview.previewId !== request.previewId) {
        return fail('import_preview_expired', '导入预览已过期');
      }
      const createdCategoryIds = [];
      const categories = preview._categories.map((category) => {
        const matches = state.categories.filter((item) => item.name === category.name);
        if (matches.length === 1) return matches[0];
        const created = { ...category, categoryId: newEventId().toUpperCase() };
        state.categories.push(created);
        createdCategoryIds.push(created.categoryId);
        return created;
      });
      const createdItems = [];
      for (const { categoryIndex, ...payload } of preview._payloads) {
        const node = {
          ...payload,
          nodeId: newEventId().toUpperCase(),
          categoryId: categories[categoryIndex]?.categoryId ?? null,
          aliasNames: [],
          hotkeyDisplay: '',
          isPinned: false,
          dragText: payload.name,
          isBuiltIn: false,
          isReadOnly: false,
          canEdit: true,
          canDelete: true,
        };
        state.nodes.push(node);
        createdItems.push(listItemFrom(node));
      }
      state.importPreview = null;
      bumpNodes('imported', createdItems.map((i) => i.nodeId));
      bumpCategories('updated', createdCategoryIds);
      return ok({ createdItems });
    },

    async ReleaseImportPreviewAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      if (state.importPreview?.previewId === request.previewId) {
        state.importPreview = null;
      }
      return ok({ released: true });
    },

    async ExportNodesAsync(request) {
      const err = requireSession(request);
      if (err) return err;
      const ids = request.nodeIds ?? [];
      if (!ids.length) return fail('validation_failed', '未选择导出节点');
      return ok({
        exportedCount: ids.length,
        fileName: 'keysender-export-mock.json',
      });
    },
  };
}
